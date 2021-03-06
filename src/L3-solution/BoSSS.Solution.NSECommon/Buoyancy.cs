﻿/* =======================================================================
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
using System.Linq;
using System.Text;
using BoSSS.Solution.Utils;

namespace BoSSS.Solution.NSECommon {

    /// <summary>
    /// [LowMach] Buoyant force.
    /// </summary>
    public class Buoyancy : NonlinearSource {

        double[] GravityDirection;
        int SpatialComponent;
        double Froude;
        MaterialLaw EoS;

        /// <summary>
        /// Ctor.
        /// </summary>
        /// <param name="GravityDirection">Unit vector for spatial direction of gravity.</param>
        /// <param name="SpatialComponent">Spatial component of source.</param>
        /// <param name="Froude">Dimensionless Froude number.</param>
        /// <param name="EoS">Equation of state for calculating density.</param>
        public Buoyancy(double[] GravityDirection, int SpatialComponent, double Froude, MaterialLaw EoS) {

            // Check direction
            double sum = 0.0;
            for (int i = 0; i < GravityDirection.Length; i++) {
                sum += GravityDirection[i] * GravityDirection[i];
            }
            double DirectionNorm = Math.Sqrt(sum);
            if ((DirectionNorm - 1.0) > 1.0e-13)
                throw new ArgumentException("Length of GravityDirection vector has to be 1.0");

            // Initialize
            this.GravityDirection = GravityDirection;
            this.SpatialComponent = SpatialComponent;
            this.Froude = Froude;
            this.EoS = EoS;
        }

        protected override double Source(double time, int j, double[] x, double[] U) {
            double src = 0.0;

            double rho = EoS.GetDensity(U[0]);
            src = 1.0 / (Froude * Froude) * rho * GravityDirection[SpatialComponent];

            return src;
        }

        /// <summary>
        /// Temperature.
        /// </summary>
        public override IList<string> ArgumentOrdering {
            get {
                return new string[] { VariableNames.Temperature };
            }
        }
    }
}
