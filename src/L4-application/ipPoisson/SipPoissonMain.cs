/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Generic;
using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.IO;
using BoSSS.Foundation.Quadrature;
using BoSSS.Solution;
using BoSSS.Solution.Utils;
using ilPSP.LinSolvers;
using ilPSP.Tracing;
using ilPSP.Utils;
using System.Diagnostics;
using MPI.Wrappers;
using BoSSS.Platform;
using ilPSP;
using System.Linq;
using BoSSS.Foundation.SpecFEM;
using BoSSS.Solution.Queries;
using BoSSS.Foundation.Grid.RefElements;
using NUnit.Framework;
using BoSSS.Solution.Multigrid;
using ilPSP.Connectors.Matlab;

namespace BoSSS.Application.SipPoisson {

    /// <summary>
    /// Benchmark application, solves a Poisson problem using the symmetric interior penalty (SIP) method.
    /// </summary>
    public class SipPoissonMain : Application<SipControl> {

#pragma warning disable 649
        /// <summary>
        /// dependent variable
        /// </summary>
        [InstantiateFromControlFile("T", "T", IOListOption.ControlFileDetermined)]
        protected SinglePhaseField T;

        /// <summary>
        /// exact solution, to determine L2-Error, see also <see cref="SipControl.ExactSolution_provided"/>.
        /// </summary>
        [InstantiateFromControlFile("Tex", "Tex", IOListOption.Never)]
        protected SinglePhaseField Tex;

        /// <summary>
        /// dependent variable
        /// </summary>
        [InstantiateFromControlFile("RHS", "T", IOListOption.ControlFileDetermined)]
        protected SinglePhaseField RHS;
#pragma warning restore 649

        /// <summary>
        /// Main routine
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args) {

            //BatchmodeConnector.Flav = BatchmodeConnector.Flavor.Octave;
            //BatchmodeConnector.MatlabExecuteable = "C:\\cygwin\\bin\\bash.exe";

            _Main(args, false, delegate () {
                SipPoissonMain p = new SipPoissonMain();

                Console.WriteLine("ipPoisson: " + ilPSP.Environment.MPIEnv.MPI_Rank + " of " + ilPSP.Environment.MPIEnv.MPI_Size
                    + " on compute node '" + ilPSP.Environment.MPIEnv.Hostname + "';");
                return p;
            });
        }

        /// <summary>
        /// 1D Nodes which can be blended between linear- and Cosine-spacing
        /// </summary>
        /// <param name="l"></param>
        /// <param name="r"></param>
        /// <param name="a">blending between linear and cosine - spacing: 1.0 = full cosine,
        /// 0.0 = full linear;</param>
        /// <param name="n">number of nodes</param>
        /// <returns></returns>
        static double[] CosLinSpaceing(double l, double r, double a, int n) {
            double[] linnodes = GenericBlas.Linspace(0, Math.PI * 0.5, n);
            double[] linnodes2 = GenericBlas.Linspace(0, 1, n);
            double[] nodes = new double[n];

            for (int i = 0; i < n; i++)
                nodes[i] = linnodes2[i] * (1 - a) + (1.0 - Math.Cos(linnodes[i])) * a;

            for (int i = 0; i < n; i++)
                nodes[i] = nodes[i] * (r - l) + l;
            return nodes;

        }

        protected override void SetInitial() {
            base.SetInitial();

            // mg coloring
            int iLevel = 0;
            foreach (var MgL in this.MultigridSequence) {
                SinglePhaseField c = new SinglePhaseField(new Basis(this.GridData, 0), "MgLevel_" + iLevel);
                Foundation.Grid.Aggregation.CoarseningAlgorithms.ColorDGField(MgL, c);
                this.MGColoring.Add(c);
                iLevel++;
            }


        }


        BlockMsrMatrix LaplaceMtx;
        double[] LaplaceAffine;

        /// <summary>
        /// Includes assembly of the matrix.
        /// </summary>
        /// <param name="L"></param>
        protected override void CreateEquationsAndSolvers(GridUpdateDataVaultBase L) {
            using (FuncTrace tr = new FuncTrace()) {

                // create operator
                // ===============
                SpatialOperator LapaceIp;
                {

                    double D = this.GridData.SpatialDimension;
                    double penalty_base = (T.Basis.Degree + 1) * (T.Basis.Degree + D) / D;
                    double penalty_factor = base.Control.penalty_poisson;

                    BoundaryCondMap<BoundaryType> PoissonBcMap = new BoundaryCondMap<BoundaryType>(this.GridData, this.Control.BoundaryValues, "T");

                    LapaceIp = new SpatialOperator(1, 1, QuadOrderFunc.SumOfMaxDegrees(), "T", "T");
                    var flux = new ipFlux(penalty_base * base.Control.penalty_poisson, this.GridData.Cells.cj, PoissonBcMap);
                    LapaceIp.EquationComponents["T"].Add(flux);

                    LapaceIp.Commit();
                }

                // Create Matrices
                // ===============

                {
                    // time measurement for matrix assembly
                    Stopwatch stw = new Stopwatch();
                    stw.Start();

                    // console
                    Console.WriteLine("creating sparse system for {0} DOF's ...", T.Mapping.Ntotal);

                    // quadrature domain
                    var volQrSch = new CellQuadratureScheme(true, CellMask.GetFullMask(this.GridData));
                    var edgQrSch = new EdgeQuadratureScheme(true, EdgeMask.GetFullMask(this.GridData));

#if DEBUG
                    // in DEBUG mode, we compare 'MsrMatrix' (old, reference implementation) and 'BlockMsrMatrix' (new standard)
                    var RefLaplaceMtx = new MsrMatrix(T.Mapping);
#endif
                    using (new BlockTrace("SipMatrixAssembly", tr)) {
                        LaplaceMtx = new BlockMsrMatrix(T.Mapping);
                        LaplaceAffine = new double[T.Mapping.LocalLength];

                        LapaceIp.ComputeMatrixEx(T.Mapping, null, T.Mapping,
                                                 LaplaceMtx, LaplaceAffine,
                                                 volQuadScheme: volQrSch, edgeQuadScheme: edgQrSch);
                    }
#if DEBUG
                    LaplaceAffine.ClearEntries();
                    LapaceIp.ComputeMatrixEx(T.Mapping, null, T.Mapping,
                                             RefLaplaceMtx, LaplaceAffine,
                                             volQuadScheme: volQrSch, edgeQuadScheme: edgQrSch);
                    MsrMatrix ErrMtx = RefLaplaceMtx.CloneAs();
                    ErrMtx.Acc(-1.0, LaplaceMtx);
                    double err = ErrMtx.InfNorm();
                    double infNrm = LaplaceMtx.InfNorm();
                    Console.WriteLine("Matrix comparison error: " + err + ", matrix norm is: " + infNrm);
                    Assert.Less(err, infNrm * 1e-10, "MsrMatrix2 comparison failed.");
#endif
                    stw.Stop();
                    Console.WriteLine("done {0} sec.", stw.Elapsed.TotalSeconds);
                }


                //double condNo = LaplaceMtx.condest(BatchmodeConnector.Flavor.Octave);
                //Console.WriteLine("condition number: {0:0.####E-00} ",condNo);

            }
        }

        /// <summary>
        /// Deprecated utility, writes matrices for spectral element method.
        /// </summary>
        public void WriteSEMMatrices() {
            int kSEM; // nodes per edge

            if (this.Grid.RefElements[0] is Triangle) {
                kSEM = this.T.Basis.Degree + 1;
            } else if (this.Grid.RefElements[0] is Square) {
                switch (this.T.Basis.Degree) {
                    case 2: kSEM = 2; break;
                    case 3: kSEM = 2; break;
                    case 4: kSEM = 3; break;
                    case 5: kSEM = 3; break;
                    case 6: kSEM = 4; break;
                    default: throw new NotSupportedException();
                }
            } else {
                throw new NotImplementedException();
            }

            SpecFemBasis SEM_basis = new SpecFemBasis(this.GridData, kSEM);

            SEM_basis.CellNodes[0].SaveToTextFile("NODES_SEM" + kSEM + ".txt");
            SEM_basis.MassMatrix.SaveToTextFileSparse("MASS_SEM" + kSEM + ".txt");

            var Mod2Nod = SEM_basis.GetModal2NodalOperator(this.T.Basis);
            var Nod2Mod = SEM_basis.GetNodal2ModalOperator(this.T.Basis);

            Mod2Nod.SaveToTextFileSparse("MODAL" + this.T.Basis.Degree + "_TO_NODAL" + kSEM + ".txt");
            Nod2Mod.SaveToTextFileSparse("NODAL" + kSEM + "_TO_MODAL" + this.T.Basis.Degree + ".txt");

            this.LaplaceMtx.SaveToTextFileSparse("OPERATOR" + this.T.Basis.Degree + ".txt");


            {
                var TEST = this.T.CloneAs();
                TEST.Clear();
                TEST.ProjectField((_2D)((x, y) => x * y));

                int J = this.GridData.Cells.NoOfCells;
                int N = SEM_basis.NodesPerCell[0];
                MultidimensionalArray TEST_at_NODES = MultidimensionalArray.Create(J, N);

                TEST.Evaluate(0, J, SEM_basis.CellNodes[0], TEST_at_NODES);

                double[] CompareAtNodes = new double[J * N];
                Mod2Nod.SpMVpara(1.0, TEST.CoordinateVector, 0.0, CompareAtNodes);

                double[] gretchen = TEST_at_NODES.ResizeShallow(J * N).To1DArray();

                double fdist = GenericBlas.L2Dist(gretchen, CompareAtNodes);
                Debug.Assert(fdist < 1.0e-8);

                double[] bak = new double[TEST.Mapping.LocalLength];
                Nod2Mod.SpMVpara(1.0, CompareAtNodes, 0.0, bak);

                double hdist = GenericBlas.L2Dist(bak, TEST.CoordinateVector);
                Debug.Assert(hdist < 1.0e-8);
            }


            var Scatter = SEM_basis.GetNodeScatterMatrix();

            Scatter.SaveToTextFileSparse("SCATTER_SEM" + kSEM + ".txt");


            {
                var SEMTEST = new SpecFemField(SEM_basis);
                var csem = SEMTEST.Coordinates;

                Random r = new Random(666);
                for (int i = 0; i < csem.GetLength(0); i++) {
                    csem[i] = r.NextDouble();
                }


                var DG_TEST0 = new SinglePhaseField(SEMTEST.Basis.ContainingDGBasis);
                var DG_TEST = this.T.CloneAs();
                DG_TEST.Clear();
                SEMTEST.AccToDGField(1.0, DG_TEST0);
                DG_TEST.AccLaidBack(1.0, DG_TEST0);

                double[] S2 = new double[Scatter.RowPartitioning.LocalLength];
                double[] S3 = new double[DG_TEST.Mapping.LocalLength];

                Scatter.SpMVpara(1.0, csem.To1DArray(), 0.0, S2);
                Nod2Mod.SpMVpara(1.0, S2, 0.0, S3);

                double gdist = GenericBlas.L2Dist(S3, DG_TEST.CoordinateVector);
                Debug.Assert(gdist < 1.0e-8);
            }
        }



        protected override double RunSolverOneStep(int TimestepNo, double phystime, double dt) {
            using (new FuncTrace()) {
                //this.WriteSEMMatrices();

                base.NoOfTimesteps = -1;
                if (TimestepNo > 1)
                    throw new ApplicationException("steady-state-equation.");

                base.TerminationKey = true;

                // call solver
                // -----------
                double mintime, maxtime;
                bool converged;
                int NoOfIterations;


                switch (base.Control.solver_name) {

                    case SolverCodes.classic_cg:
                    case SolverCodes.classic_mumps:
                    case SolverCodes.classic_pardiso:
                    ClassicSolve(out mintime, out maxtime, out converged, out NoOfIterations);
                    break;

                    default:
                    ExperimentalSolve(out mintime, out maxtime, out converged, out NoOfIterations);
                    break;
                }

                Console.WriteLine("finished; " + NoOfIterations + " iterations.");
                Console.WriteLine("converged? " + converged);
                Console.WriteLine("Timespan: " + mintime + " to " + maxtime + " seconds");


                base.QueryHandler.ValueQuery("minSolRunT", mintime, true);
                base.QueryHandler.ValueQuery("maxSolRunT", maxtime, true);
                base.QueryHandler.ValueQuery("Conv", converged ? 1.0 : 0.0, true);
                base.QueryHandler.ValueQuery("NoIter", NoOfIterations, true);
                base.QueryHandler.ValueQuery("NoOfCells", this.GridData.CellPartitioning.TotalLength);
                base.QueryHandler.ValueQuery("DOFs", T.Mapping.TotalLength, true);
                base.QueryHandler.ValueQuery("BlockSize", T.Basis.Length, true);


                if (base.Control.ExactSolution_provided) {
                    SinglePhaseField ERR;
                    if (Tex.Basis.Degree >= T.Basis.Degree) {
                        ERR = this.Tex.CloneAs();
                        ERR.AccLaidBack(-1.0, T);
                    } else {
                        ERR = this.T.CloneAs();
                        ERR.AccLaidBack(-1.0, Tex);
                    }

                    double L2_ERR = ERR.L2Norm();

                    base.QueryHandler.ValueQuery("SolL2err", L2_ERR, true);

                }


                return 0.0;
            }
        }

        private void ClassicSolve(out double mintime, out double maxtime, out bool Converged, out int NoOfIter) {

            mintime = double.MaxValue;
            maxtime = double.MinValue;
            Converged = false;
            NoOfIter = int.MaxValue;

            for (int i = 0; i < base.Control.NoOfSolverRuns; i++) {

                // create sparse solver
                // --------------------
                ISparseSolver ipSolver;
                switch (base.Control.solver_name) {
                    case SolverCodes.classic_pardiso:
                    ipSolver = new ilPSP.LinSolvers.PARDISO.PARDISOSolver() {
                        CacheFactorization = true,
                        UseDoublePrecision = true
                    };
                    break;

                    case SolverCodes.classic_mumps:
                    ipSolver = new ilPSP.LinSolvers.MUMPS.MUMPSSolver();
                    break;

                    case SolverCodes.classic_cg:
                    ipSolver = new ilPSP.LinSolvers.monkey.CG() {
                        MaxIterations = 1000000,
                        Tolerance = 1.0e-10,
                        DevType = ilPSP.LinSolvers.monkey.DeviceType.Cuda
                    };
                    break;

                    default:
                    throw new ArgumentException();
                }
                ipSolver.DefineMatrix(LaplaceMtx);

                // call solver
                // -----------

                int NoOfSolverRuns = base.Control.NoOfSolverRuns;
                T.Clear();

                Console.WriteLine("RUN " + i + ": solving system...");

                var RHSvec = RHS.CoordinateVector.ToArray();
                //RHSvec.SaveToTextFile("DG" + this.T.Basis.Degree + "_RHS.txt");
                BLAS.daxpy(RHSvec.Length, -1.0, this.LaplaceAffine, 1, RHSvec, 1);

                T.Clear();
                var solRes = default(SolverResult);
                solRes = ipSolver.Solve(T.CoordinateVector, RHSvec);
                mintime = Math.Min(solRes.RunTime.TotalSeconds, mintime);
                maxtime = Math.Max(solRes.RunTime.TotalSeconds, maxtime);

                //T.CoordinatesAsVector.SaveToTextFile("DG" + this.T.Basis.Degree + "_SOLUTION.txt");

                Converged = solRes.Converged;
                NoOfIter = solRes.NoOfIterations;

                Console.WriteLine("Pardiso phase 11: " + ilPSP.LinSolvers.PARDISO.PARDISOSolver.Phase_11.Elapsed.TotalSeconds);
                Console.WriteLine("Pardiso phase 22: " + ilPSP.LinSolvers.PARDISO.PARDISOSolver.Phase_22.Elapsed.TotalSeconds);
                Console.WriteLine("Pardiso phase 33: " + ilPSP.LinSolvers.PARDISO.PARDISOSolver.Phase_33.Elapsed.TotalSeconds);


                ipSolver.Dispose();
            }
        }

        List<DGField> MGColoring = new List<DGField>();


        MultigridOperator.ChangeOfBasisConfig[][] MgConfig {
            get {
                int p = this.T.Basis.Degree;
                int NoOfLevels = this.MultigridSequence.Length;
                var config = new MultigridOperator.ChangeOfBasisConfig[NoOfLevels][];

                for (int iLevel = 0; iLevel < NoOfLevels; iLevel++) {

                    config[iLevel] = new MultigridOperator.ChangeOfBasisConfig[] {
                        new MultigridOperator.ChangeOfBasisConfig() {
                            VarIndex = new int[] {0},
                            mode = MultigridOperator.Mode.DiagBlockEquilib,
                            Degree = Math.Max(1, p - iLevel)
                        }
                    };

                }

                return config;
            }

        }


        private void ExperimentalSolve(out double mintime, out double maxtime, out bool Converged, out int NoOfIter) {
            using (var tr = new FuncTrace()) {
                int p = this.T.Basis.Degree;
                var MgSeq = this.MultigridSequence;
                mintime = double.MaxValue;
                maxtime = 0;
                Converged = false;
                NoOfIter = int.MaxValue;

                Console.WriteLine("Construction of Multigrid basis...");
                Stopwatch mgBasis = new Stopwatch();
                mgBasis.Start();
                AggregationGridBasis[][] AggBasis;
                using (new BlockTrace("Aggregation_basis_init", tr)) {
                    AggBasis = AggregationGridBasis.CreateSequence(MgSeq, new Basis[] { this.T.Basis }); 
                }
                mgBasis.Stop();
                Console.WriteLine("done. (" + mgBasis.Elapsed.TotalSeconds + " sec)");


                //foreach (int sz in new int[] { 1000, 2000, 5000, 10000, 20000 }) {
                //    base.Control.TargetBlockSize = sz;

                for (int irun = 0; irun < base.Control.NoOfSolverRuns; irun++) {
                    Stopwatch stw = new Stopwatch();
                    stw.Reset();
                    stw.Start();

                    Console.WriteLine("Setting up multigrid operator...");
                    var mgsetup = new Stopwatch();
                    mgsetup.Start();
                    var MultigridOp = new MultigridOperator(AggBasis, this.T.Mapping, this.LaplaceMtx, null, MgConfig);
                    mgsetup.Stop();
                    Console.WriteLine("done. (" + mgsetup.Elapsed.TotalSeconds + " sec)");


                    Console.WriteLine("Setting up solver...");
                    var solverSetup = new Stopwatch();
                    solverSetup.Start();
                    ISolverSmootherTemplate solver;
                    switch (base.Control.solver_name) {
                        case SolverCodes.exp_direct:
                        solver = new DirectSolver() {
                            WhichSolver = DirectSolver._whichSolver.PARDISO
                        };
                        break;

                        case SolverCodes.exp_direct_lapack:
                        solver = new DirectSolver() {
                            WhichSolver = DirectSolver._whichSolver.Lapack
                        };
                        break;

                        case SolverCodes.exp_softpcg_schwarz_directcoarse: {
                            double LL = this.LaplaceMtx._RowPartitioning.LocalLength;
                            int NoOfBlocks = (int)Math.Max(1, Math.Round(LL / (double)this.Control.TargetBlockSize));
                            Console.WriteLine("Additive Schwarz w. direct coarse, No of blocks: " + NoOfBlocks.MPISum());
                            solver = new SoftPCG() {
                                m_MaxIterations = 50000,
                                m_Tolerance = 1.0e-10,
                                Precond = new Schwarz() {
                                    m_MaxIterations = 1,
                                    CoarseSolver = new GenericRestriction() {
                                        CoarserLevelSolver = new GenericRestriction() {
                                            CoarserLevelSolver = new DirectSolver() {
                                                WhichSolver = DirectSolver._whichSolver.PARDISO
                                            }
                                        }
                                    },
                                    m_BlockingStrategy = new Schwarz.METISBlockingStrategy() {
                                        NoOfPartsPerProcess = NoOfBlocks
                                    },
                                    Overlap = 1,

                                }
                            };
                            break;
                        }

                        case SolverCodes.exp_softpcg_schwarz: {
                            double LL = this.LaplaceMtx._RowPartitioning.LocalLength;
                            int NoOfBlocks = (int)Math.Max(1, Math.Round(LL / (double)this.Control.TargetBlockSize));
                            Console.WriteLine("Additive Schwarz, No of blocks: " + NoOfBlocks.MPISum());

                            solver = new SoftPCG() {
                                m_MaxIterations = 50000,
                                m_Tolerance = 1.0e-10,
                                Precond = new Schwarz() {
                                    m_MaxIterations = 1,
                                    CoarseSolver = null,
                                    m_BlockingStrategy = new Schwarz.METISBlockingStrategy {
                                        NoOfPartsPerProcess = NoOfBlocks
                                    },
                                    Overlap = 1
                                }
                            };
                            break;
                        }

                        case SolverCodes.exp_softpcg_mg:
                        solver = MultilevelSchwarz(MultigridOp);
                        break;


                        case SolverCodes.exp_Kcycle_schwarz:
                        solver = KcycleMultiSchwarz(MultigridOp);
                        break;

                        default:
                        throw new ApplicationException("unknown solver: " + this.Control.solver_name);
                    }

                    T.Clear();
                    T.AccLaidBack(1.0, Tex);
                    ConvergenceObserver CO = null;
                    //CO = new ConvergenceObserver(MultigridOp, null, T.CoordinateVector.ToArray());
                    //CO.TecplotOut = "oasch";
                    if (solver is ISolverWithCallback) {

                        if (CO == null) {
                            ((ISolverWithCallback)solver).IterationCallback = delegate (int iter, double[] xI, double[] rI, MultigridOperator mgOp) {
                                double l2_RES = rI.L2NormPow2().MPISum().Sqrt();

                                double[] xRef = new double[xI.Length];
                                MultigridOp.TransformSolInto(T.CoordinateVector, xRef);

                                double l2_ERR = GenericBlas.L2DistPow2(xI, xRef).MPISum().Sqrt();
                                Console.WriteLine("Iter: {0}\tRes: {1:0.##E-00}\tErr: {2:0.##E-00}\tRunt: {3:0.##E-00}", iter, l2_RES, l2_ERR, stw.Elapsed.TotalSeconds);
                                //Tjac.CoordinatesAsVector.SetV(xI);
                                //Residual.CoordinatesAsVector.SetV(rI);
                                //PlotCurrentState(iter, new TimestepNumber(iter), 3);
                            };
                        } else {
                            ((ISolverWithCallback)solver).IterationCallback = CO.IterationCallback;
                        }
                    }


                    using (new BlockTrace("Solver_Init", tr)) {
                        solver.Init(MultigridOp);
                    }
                    solverSetup.Stop();
                    Console.WriteLine("done. (" + solverSetup.Elapsed.TotalSeconds + " sec)");

                    Console.WriteLine("Running solver...");
                    var solverIteration = new Stopwatch();
                    solverIteration.Start();
                    double[] T2 = this.T.CoordinateVector.ToArray();
                    using (new BlockTrace("Solver_Run", tr)) {
                        solver.ResetStat();
                        T2.Clear();
                        var RHSvec = RHS.CoordinateVector.ToArray();
                        BLAS.daxpy(RHSvec.Length, -1.0, this.LaplaceAffine, 1, RHSvec, 1);
                        MultigridOp.UseSolver(solver, T2, RHSvec);
                        T.CoordinateVector.SetV(T2);
                    }
                    solverIteration.Stop();
                    Console.WriteLine("done. (" + solverIteration.Elapsed.TotalSeconds + " sec)");

                    Console.WriteLine("Pardiso phase 11: " + ilPSP.LinSolvers.PARDISO.PARDISOSolver.Phase_11.Elapsed.TotalSeconds);
                    Console.WriteLine("Pardiso phase 22: " + ilPSP.LinSolvers.PARDISO.PARDISOSolver.Phase_22.Elapsed.TotalSeconds);
                    Console.WriteLine("Pardiso phase 33: " + ilPSP.LinSolvers.PARDISO.PARDISOSolver.Phase_33.Elapsed.TotalSeconds);

                    // time measurement, statistics
                    stw.Stop();
                    mintime = Math.Min(stw.Elapsed.TotalSeconds, mintime);
                    maxtime = Math.Max(stw.Elapsed.TotalSeconds, maxtime);
                    Converged = solver.Converged;
                    NoOfIter = solver.ThisLevelIterations;

                    if (CO != null)
                        CO.PlotTrend(true, true, true);

                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        ISolverSmootherTemplate KcycleMultiSchwarz(MultigridOperator op) {
            var solver = new OrthonormalizationScheme() {
                MaxIter = 500,
                Tolerance = 1.0e-10,
                
            };

            // my tests show that the ideal block size may be around 10'000
            int DirectKickIn = base.Control.TargetBlockSize;


            MultigridOperator Current = op;
            var PrecondChain = new List<ISolverSmootherTemplate>();
            for(int iLevel = 0; iLevel < base.MultigridSequence.Length; iLevel++) {
                int SysSize = Current.Mapping.TotalLength;
                int NoOfBlocks = (int) Math.Ceiling(((double)SysSize) / ((double)DirectKickIn));

                bool useDirect = false;
                useDirect |= (SysSize < DirectKickIn);
                useDirect |= iLevel == base.MultigridSequence.Length - 1;
                useDirect |= NoOfBlocks.MPISum() <= 1;


                ISolverSmootherTemplate levelSolver;
                if (useDirect) {
                    levelSolver = new DirectSolver() {
                        WhichSolver = DirectSolver._whichSolver.PARDISO,
                        TestSolution = false
                    };
                } else {
                                        
                    Schwarz swz1 = new Schwarz() {
                        m_MaxIterations = 1,
                        CoarseSolver = null,
                        m_BlockingStrategy = new Schwarz.METISBlockingStrategy() {
                            NoOfPartsPerProcess = NoOfBlocks
                        },
                        Overlap = 2 // overlap seems to help
                    };

                    SoftPCG pcg1 = new SoftPCG() {
                        m_MinIterations = 5,
                        m_MaxIterations = 5
                    };

                    //*/

                    var pre = new SolverSquence() {
                        SolverChain = new ISolverSmootherTemplate[] { swz1, pcg1 }
                    };

                    levelSolver = swz1;
                }

                if(iLevel > 0) {

                    GenericRestriction[] R = new GenericRestriction[iLevel];
                    for(int ir = 0; ir < R.Length; ir++) {
                        R[ir] = new GenericRestriction();
                        if (ir >= 1)
                            R[ir - 1].CoarserLevelSolver = R[ir];
                    }
                    R[iLevel - 1].CoarserLevelSolver = levelSolver;
                    PrecondChain.Add(R[0]);

                } else {
                    PrecondChain.Add(levelSolver);
                }


                if (useDirect) {
                    Console.WriteLine("Kswz: using {0} levels, lowest level DOF is {1}, target size is {2}.", iLevel + 1, SysSize, DirectKickIn);
                    break;
                }



                Current = Current.CoarserLevel;

            }

            
            if (PrecondChain.Count > 1) {
                /*
                // construct a V-cycle
                for (int i = PrecondChain.Count - 2; i>= 0; i--) {
                    PrecondChain.Add(PrecondChain[i]);
                }
                */

                var tmp = PrecondChain.ToArray();
                for(int i = 0; i < PrecondChain.Count; i++) {
                    PrecondChain[i] = tmp[PrecondChain.Count - 1 - i];
                }
            }
            


            solver.PrecondS = PrecondChain.ToArray();
            solver.MaxKrylovDim = solver.PrecondS.Length * 4;
            
            return solver;
        }

        /// <summary>
        /// Ganz ok.
        /// </summary>
        ISolverSmootherTemplate MultilevelSchwarz(MultigridOperator op) {
            var solver = new SoftPCG() {
                m_MaxIterations = 500,
                m_Tolerance = 1.0e-12
            };
            //var solver = new OrthonormalizationScheme() {
            //    MaxIter = 500,
            //    Tolerance = 1.0e-10,
            //};
            //var solver = new SoftGMRES() {
            //    m_MaxIterations = 500,
            //    m_Tolerance = 1.0e-10,

            //};

            // my tests show that the ideal block size may be around 10'000
            int DirectKickIn = base.Control.TargetBlockSize;


            MultigridOperator Current = op;
            ISolverSmootherTemplate[] MultigridChain = new ISolverSmootherTemplate[base.MultigridSequence.Length];
            for(int iLevel = 0; iLevel < base.MultigridSequence.Length; iLevel++) {
                int SysSize = Current.Mapping.TotalLength;
                int NoOfBlocks = (int) Math.Ceiling(((double)SysSize) / ((double)DirectKickIn));

                bool useDirect = false;
                useDirect |= (SysSize < DirectKickIn);
                useDirect |= iLevel == base.MultigridSequence.Length - 1;
                useDirect |= NoOfBlocks.MPISum() <= 1;
                
                if (useDirect) {
                    MultigridChain[iLevel] = new DirectSolver() {
                        WhichSolver = DirectSolver._whichSolver.PARDISO,
                        TestSolution = false
                    };
                } else {

                    ClassicMultigrid MgLevel = new ClassicMultigrid() {
                        m_MaxIterations = 1,
                        m_Tolerance = 0.0 // termination controlled by top level PCG
                    };


                    MultigridChain[iLevel] = MgLevel;

                    

                    
                    //Schwarz swz2 = new Schwarz() {
                    //    m_MaxIterations = 1,
                    //    CoarseSolver = null,
                    //    m_BlockingStrategy = new Schwarz.METISBlockingStrategy() {
                    //        NoOfParts = NoOfBlocks
                    //    },
                    //    Overlap = 0
                    //};
                    
                    
                    
                    //*/

                   

                    


                    ISolverSmootherTemplate pre, pst;
                    if(iLevel > 0) {

                        Schwarz swz1 = new Schwarz() {
                            m_MaxIterations = 1,
                            CoarseSolver = null,
                            m_BlockingStrategy = new Schwarz.METISBlockingStrategy() {
                                NoOfPartsPerProcess = NoOfBlocks
                            },
                            Overlap = 0 // overlap does **NOT** seem to help
                        };

                        SoftPCG pcg1 = new SoftPCG() {
                            m_MinIterations = 5,
                            m_MaxIterations = 5
                        };

                        SoftPCG pcg2 = new SoftPCG() {
                            m_MinIterations = 5,
                            m_MaxIterations = 5
                        };

                        var preChain = new ISolverSmootherTemplate[] { swz1, pcg1 };
                        var pstChain = new ISolverSmootherTemplate[] { swz1, pcg2 };

                        pre = new SolverSquence() { SolverChain = preChain };
                        pst = new SolverSquence() { SolverChain = pstChain };
                    } else {
                        // +++++++++++++++++++++++++++++++++++++++++++++++++++
                        // top level - use only iterative (non-direct) solvers
                        // +++++++++++++++++++++++++++++++++++++++++++++++++++

                        pre = new BlockJacobi() {
                            NoOfIterations = 3,
                            omega = 0.5
                        };

                        pst = new BlockJacobi() {
                            NoOfIterations = 3,
                            omega = 0.5
                        };

                        //preChain = new ISolverSmootherTemplate[] { pcg1 };
                        //pstChain = new ISolverSmootherTemplate[] { pcg2 };
                    }


                    


                    //if (iLevel > 0) {
                    //    MgLevel.PreSmoother = pre;
                    //    MgLevel.PostSmoother = pst;
                    //} else {
                    //    //MgLevel.PreSmoother = pcg1;   // ganz schlechte Idee, konvergiert gegen FALSCHE l�sung
                    //    //MgLevel.PostSmoother = pcg2;  // ganz schlechte Idee, konvergiert gegen FALSCHE l�sung
                    //    MgLevel.PreSmoother = pre;
                    //    MgLevel.PostSmoother = pst;
                    //}

                    MgLevel.PreSmoother = pre;
                    MgLevel.PostSmoother = pst;
                }

                if(iLevel > 0) {
                    ((ClassicMultigrid)(MultigridChain[iLevel - 1])).CoarserLevelSolver = MultigridChain[iLevel];
                }

                if (useDirect) {
                    Console.WriteLine("MG: using {0} levels, lowest level DOF is {1}, target size is {2}.", iLevel + 1, SysSize, DirectKickIn);
                    break;
                }



                Current = Current.CoarserLevel;

            } // end of level loop


            solver.Precond = MultigridChain[0];
            //solver.PrecondS = new[] { MultigridChain[0] };
            
            return solver;
        }



        protected override void Bye() {
            object SolL2err;
            if (this.QueryHandler.QueryResults.TryGetValue("SolL2err", out SolL2err)) {
                Console.WriteLine("Value of Query 'SolL2err' " + SolL2err.ToString());
            } else {
                Console.WriteLine("query 'SolL2err' not found.");
            }
        }



        protected override void PlotCurrentState(double phystime, TimestepNumber timestepNo, int superSampling = 0) {
            BoSSS.Solution.Tecplot.Tecplot.PlotFields(new DGField[] { T, Tex, RHS }, "poisson" + timestepNo, phystime, superSampling);
            BoSSS.Solution.Tecplot.Tecplot.PlotFields(ArrayTools.Cat(new DGField[] { T, Tex, RHS }, this.MGColoring), "poisson_grid" + timestepNo, phystime, 0);
        }
    
    }

    /// <summary>
    /// Interior Penalty Flux
    /// </summary>
    class ipFlux : BoSSS.Solution.NSECommon.ipLaplace {

        public ipFlux(double penalty_const, MultidimensionalArray cj, BoundaryCondMap<BoundaryType> __boundaryCondMap)
            : base(penalty_const, cj, "T") //
        {
            m_boundaryCondMap = __boundaryCondMap;
            m_bndFunc = m_boundaryCondMap.bndFunction["T"];
        }

        BoundaryCondMap<BoundaryType> m_boundaryCondMap;
        Func<double[], double, double>[] m_bndFunc;

        protected override double g_Diri(ref CommonParamsBnd inp) {
            double v = m_bndFunc[inp.EdgeTag](inp.X, inp.time);
            return v;
        }

        protected override double g_Neum(ref CommonParamsBnd inp) {
            double v = m_bndFunc[inp.EdgeTag](inp.X, inp.time);
            return v;
        }
        
        protected override bool IsDirichlet(ref CommonParamsBnd inp) {
            BoundaryType edgeType = m_boundaryCondMap.EdgeTag2Type[inp.EdgeTag];
            switch(edgeType) {
                case BoundaryType.Dirichlet:
                return true;

                case BoundaryType.Neumann:
                return false;

                default:
                throw new NotImplementedException();
            }
        }
    }


}
