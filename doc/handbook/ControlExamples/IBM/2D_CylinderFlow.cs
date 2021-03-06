﻿using System;
using System.Collections.Generic;
using BoSSS.Platform;
using BoSSS.Solution.Control;
using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using System.Diagnostics;
using BoSSS.Solution.Multigrid;
using ilPSP.Utils;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.Grid.RefElements;
using BoSSS.Application.IBM_Solver;

var c = new IBM_Control();

// Path to database
c.DbPath = @"c:\tmp\IBM_db";

// Polynomial degress
int k=2;

// Domain and Grid variables
double xMin        = -2; 
double xMax        = 20;
double yMin        = -2; 
double yMax        = 2.1;
int numberOfCellsX = 44; 
int numberOfCellsY = 8;

// Create a new control object for setting up the simulation
c.savetodb = true;
c.saveperiod = 1;

// Setting some variables for database saving. Here it is also possible to define tags which can be helpful 
// for finding a particular simulation in the BoSSS database
string sessionName   = "dt = 1E20_" + numberOfCellsX + "x" + numberOfCellsY + "_k" + k;
c.SessionName        = sessionName;
c.ProjectName        = sessionName;
c.ProjectDescription = sessionName;
c.Tags.Add("numberOfCellsX_" + numberOfCellsX);
c.Tags.Add("numberOfCellsY_" + numberOfCellsY);
c.Tags.Add("k"+k);

// Here, the degree for each field is set (degree k for velocity, k-1 for pressure). 
// First, Phi is not going to be used in this simulation but it has to be specified anyways.
c.FieldOptions.Add("VelocityX", new FieldOpts() {
Degree = k,
SaveToDB = FieldOpts.SaveToDBOpt.TRUE 
});
c.FieldOptions.Add("VelocityY", new FieldOpts() {
Degree = k,
SaveToDB = FieldOpts.SaveToDBOpt.TRUE
});
c.FieldOptions.Add("Pressure", new FieldOpts() {
Degree = k - 1,
SaveToDB = FieldOpts.SaveToDBOpt.TRUE 
});
c.FieldOptions.Add("PhiDG", new FieldOpts() {
Degree = 2,
SaveToDB = FieldOpts.SaveToDBOpt.TRUE
});
c.FieldOptions.Add("Phi", new FieldOpts() {
Degree = 2,
SaveToDB = FieldOpts.SaveToDBOpt.TRUE
});

// Grid generation and setting the boundary conditions
c.GridFunc = delegate {
	// A refined grid at the cylinder is needed in order to yield good results
	
	//x-Direction (using also hyperbolic tangential distribution)
	var _xNodes1 = Grid1D.TanhSpacing(-2, -1, 10, 0.5, false); 
	_xNodes1     = _xNodes1.GetSubVector(0, (_xNodes1.Length - 1));
	var _xNodes2 = GenericBlas.Linspace(-1, 2, 35); 
	_xNodes2     = _xNodes2.GetSubVector(0, (_xNodes2.Length - 1));
	var _xNodes3 = Grid1D.TanhSpacing(2, 20, 60 , 1.5, true);  
	var xNodes   = ArrayTools.Cat(_xNodes1, _xNodes2, _xNodes3);
	
	//y-Direction
	var _yNodes1 = Grid1D.TanhSpacing(-2, -1, 7, 0.9, false); 
	_yNodes1     = _yNodes1.GetSubVector(0, (_yNodes1.Length - 1));
	var _yNodes2 = GenericBlas.Linspace(-1, 1, 25); 
	_yNodes2     = _yNodes2.GetSubVector(0, (_yNodes2.Length - 1));
	var _yNodes3 = Grid1D.TanhSpacing(1, 2.1, 7, 1.1, true);  
	var yNodes   = ArrayTools.Cat(_yNodes1, _yNodes2, _yNodes3);
	
	GridCommons grid = Grid2D.Cartesian2DGrid(xNodes, yNodes, CellType.Square_Linear, false);
	
	grid.EdgeTagNames.Add(1, "wall"); 
	grid.EdgeTagNames.Add(2, "Velocity_Inlet"); 
	grid.EdgeTagNames.Add(3, "Pressure_Outlet");
	
	grid.DefineEdgeTags(delegate (double[] X) {
	byte et = 0;
	if (Math.Abs(X[1] - (-2)) <= 1.0e-8)
		et = 1;
	if (Math.Abs(X[1] - (+2.1 )) <= 1.0e-8)
		et = 1;
	if (Math.Abs(X[0] - (-2)) <= 1.0e-8)
		et = 2;
	if (Math.Abs(X[0] - (+20.0)) <= 1.0e-8)
		et = 3;
	return et;
	});
	
	return grid;
}

// Specification of boundary conditions with a parabolic velocity profile for the inlet
c.AddBoundaryCondition("wall");
c.AddBoundaryCondition("Velocity_Inlet", "VelocityX", X => (4.1 * 1.5 * (X[1] + 2) * (4.1 - (X[1] + 2)) / (4.1 * 4.1))); 
c.AddBoundaryCondition("Pressure_Outlet");

// Fluid Properties
// Note: As characteristic length and fluid density are choosen to one. The viscosity can be defined by $1/reynolds$.
double reynolds            = 20;
c.PhysicalParameters.rho_A = 1;
c.PhysicalParameters.mu_A  = 1.0/reynolds;

// Bool parameter wheather the Navier-Stokes or Stokes equations should be solved
c.PhysicalParameters.IncludeConvection = true;

// Initial Values are set to 0
c.InitialValues.Clear();
c.InitialValues.Add("VelocityX",new Formula("X => 0.0", false));
c.InitialValues.Add("VelocityY",new Formula("X => 0.0", false));
c.InitialValues.Add("Pressure",new Formula("X => 0.0", false));

// Phi is a level set variable which is used to represent a cylinder with radius 0.5 in this example
c.InitialValues.Add("Phi", new Formula("X => -(X[0]).Pow2() + -(X[1]).Pow2() + 0.25", false));

// Properties for the Picard Iterations
c.MaxSolverIterations = 50;
c.MinSolverIterations = 1;

// Timestepping properties:
// We will simulate the steady state solution by using just one timestep of 1E20 seconds of physical time for this instationary simulation.
c.Timestepper_Scheme = IBM_Control.TimesteppingScheme.ImplicitEuler;
double dt            = 1E20;
c.dtMax              = dt;
c.dtMin              = dt;
c.Endtime            = 64;
c.NoOfTimesteps      = 1;

c;