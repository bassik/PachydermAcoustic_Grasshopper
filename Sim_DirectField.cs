﻿//'Pachyderm-Acoustic: Geometrical Acoustics for Rhinoceros (GPL) by Arthur van der Harten 
//' 
//'This file is part of Pachyderm-Acoustic. 
//' 
//'Copyright (c) 2008-2015, Arthur van der Harten 
//'Pachyderm-Acoustic is free software; you can redistribute it and/or modify 
//'it under the terms of the GNU General Public License as published 
//'by the Free Software Foundation; either version 3 of the License, or 
//'(at your option) any later version. 
//'Pachyderm-Acoustic is distributed in the hope that it will be useful, 
//'but WITHOUT ANY WARRANTY; without even the implied warranty of 
//'MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the 
//'GNU General Public License for more details. 
//' 
//'You should have received a copy of the GNU General Public 
//'License along with Pachyderm-Acoustic; if not, write to the Free Software 
//'Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA. 

using System;
using System.Collections.Generic;
using Pachyderm_Acoustic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace PachydermGH
{
    public class QuickDirect : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public QuickDirect()
            : base("Pach_Direct", "Direct F-Domain",
                "Maps direct sound to a mesh, including phase and air attenuation.",
                "Acoustics", "Computation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Source Object", "S", "The Pachyderm sound source object.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Medium Properties", "P", "The Pachyderm medium properties object", GH_ParamAccess.item);
            pManager.AddMeshParameter("Receivers", "R", "The points or mesh to use for mapping", GH_ParamAccess.item);
            pManager.AddIntervalParameter("Frequency Scope", "Oct", "An interval of the first and last octave to calculate (0 = 62.5 Hz, 1 = 125 HZ., ..., 7 = 8000 Hz.", GH_ParamAccess.item);

            Grasshopper.Kernel.Parameters.Param_GenericObject param = (pManager[1] as Grasshopper.Kernel.Parameters.Param_GenericObject);
            if (param != null) param.SetPersistentData(new Pachyderm_Acoustic.Environment.Uniform_Medium(0, 100000, 20+273.15, 50, false));

            Grasshopper.Kernel.Parameters.Param_Interval param2 = (pManager[3] as Grasshopper.Kernel.Parameters.Param_Interval);
            if (param2 != null) param2.SetPersistentData(new Grasshopper.Kernel.Types.GH_Interval(new Interval(0,7)));
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Pressure Field", "P", "Pressure field calculation for each point on the mesh.", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Pachyderm_Acoustic.Environment.Source> Src = new List<Pachyderm_Acoustic.Environment.Source>();
            Mesh M = new Mesh();
            int LF = 0, HF = 0;
            Pachyderm_Acoustic.Environment.Uniform_Medium MP = new Pachyderm_Acoustic.Environment.Uniform_Medium(0, 101325, 293.15, 50, false);
            DA.GetDataList<Pachyderm_Acoustic.Environment.Source>(0, Src);
            DA.GetData<Pachyderm_Acoustic.Environment.Uniform_Medium>(1, ref MP);
            DA.GetData<Mesh>(2, ref M);
            DA.GetData<int>(3, ref LF);
            DA.GetData<int>(4, ref HF);

            int No_of_octaves = HF - LF + 1;
            if (No_of_octaves < 0) return;

            Point3d[] Pts = M.Vertices.ToPoint3dArray();
            double[] P_Sum = new double[Pts.Length];

            Random Rnd = new Pachyderm_Acoustic.Utilities.PachTools.RandomNumberGenerator();

            double c = MP.Sound_Speed(Pts[0]);

            double[] lambda2pi = new double[8]{ 2 * Math.PI * 62.5 / c, 2 / Math.PI * 125 / c, 2 / Math.PI * 250 / c, 2 * Math.PI * 500 / c, 2 * Math.PI * 1000 / c, 2 * Math.PI * 2000 / c, 2 * Math.PI * 4000 / c, 2 * Math.PI * 8000 / c };

            System.Threading.Tasks.Parallel.For(0, Pts.Length, i =>
            {
                double[] P_Real = new double[No_of_octaves], P_Imag = new double[No_of_octaves];
                for (int S_id = 0; S_id < Src.Count; S_id++)
                {
                    Vector3d V = Pts[i] - Src[S_id].Origin();
                    double Length = V.Length;
                    int id;
                    id = Rnd.Next();
                    double delay = Src[S_id].Delay * c;
                    V.Unitize();
                    double[] Power = Src[S_id].DirPower(0, id, new Hare.Geometry.Vector(V.X, V.Y, V.Z));
                    for (int oct = 0; oct < No_of_octaves; oct++)
                    {
                        double I = Power[oct+LF] * Math.Pow(10, -MP.Attenuation_Coef(0)[oct+LF] * Length) / (4 * Math.PI * Length * Length);
                        double real, imag;
                        Pachyderm_Acoustic.Utilities.Numerics.ExpComplex(0, (lambda2pi[oct+LF] * (Length + delay) + Src[S_id].Phase(new Hare.Geometry.Vector(V.X, V.Y, V.Z))[oct+LF]), out real, out imag);
                        P_Real[oct] += Math.Sqrt(I * MP.Rho_C(Pachyderm_Acoustic.Utilities.PachTools.RPttoHPt(Pts[i]))) * real;
                        P_Imag[oct] += Math.Sqrt(I * MP.Rho_C(Pachyderm_Acoustic.Utilities.PachTools.RPttoHPt(Pts[i]))) * imag;
                    }
                }

                for (int oct = 0; oct < No_of_octaves; oct++) 
                {
                    double P_Mag = Pachyderm_Acoustic.Utilities.Numerics.Abs(P_Real[oct], P_Imag[oct]);
                    P_Sum[i] += P_Mag * P_Mag;
                }

                P_Sum[i] = Math.Sqrt(P_Sum[i]);
            });

            DA.SetDataList(0, P_Sum);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("{9181a793-5fe1-4fd5-bd94-eb1825127c9f}"); }
        }
    }
}