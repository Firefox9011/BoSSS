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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BoSSS.Foundation.Grid {

    /// <summary>
    /// One chunk of a execution mask (see <see cref="ExecutionMask"/>);
    /// </summary>
    public struct Chunk : IEquatable<Chunk> {

        /// <summary>
        /// local element (cell or edge) index at which the integration 
        /// should start;
        /// </summary>
        public int i0;

        /// <summary>
        /// number of elements (cells or edges) to perform quadrature
        /// on;
        /// </summary>
        public int Len;

        /// <summary>
        /// sum of <see cref="i0"/> and <see cref="Len"/>.
        /// </summary>
        public int JE {
            get {
                return (i0 + Len);
            }
        }

        /// <summary>
        /// Enumeration of all element indices contained in this chunk
        /// </summary>
        public IEnumerable<int> Elements {
            get {
                for (int i = 0; i < Len; i++) {
                    yield return i + i0;
                }
            }
        }

        /// <summary>
        /// See <see cref="Equals(Chunk)"/>
        /// </summary>
        /// <param name="obj">See <see cref="object.Equals(object)"/></param>
        /// <returns>See <see cref="Equals(Chunk)"/></returns>
        public override bool Equals(object obj) {
            if (obj is Chunk) {
                return Equals((Chunk)obj);
            } else {
                return false;
            }
        }

        /// <summary>
        /// Builds a hash based on <see cref="i0"/> and <see cref="Len"/>
        /// </summary>
        /// <returns>A hash code</returns>
        public override int GetHashCode() {
            return i0 ^ Len;
        }

        #region IEquatable<Chunk> Members

        /// <summary>
        /// Checks whether this chunk is equal to the given chunk by comparing
        /// <see cref="i0"/> and <see cref="Len"/>.
        /// </summary>
        /// <param name="other">
        /// The chunk for which equality should be checked.
        /// </param>
        /// <returns>
        /// True, if <see cref="i0"/> and <see cref="Len"/> are equal for both
        /// chunks. False, otherwise.
        /// </returns>
        public bool Equals(Chunk other) {
            return (i0 == other.i0 && Len == other.Len);
        }

        #endregion
        /// <summary>
        /// Creates a chunk of length 1.
        /// </summary>
        /// <param name="element">
        /// The only element in the chunk.
        /// </param>
        /// <returns>
        /// A chunk which includes <paramref name="element"/> only.
        /// </returns>
        [DebuggerStepThrough]
        public static Chunk GetSingleElementChunk(int element) {
            return new Chunk() {
                i0 = element,
                Len = 1
            };
        }
    }

    /// <summary>
    /// The grid mask is one core part of the subgrid framework;
    /// The <see cref="SubGrid"/>-object uses
    /// cell masks (see <see cref="CellMask"/>)
    /// and edge masks (see <see cref="EdgeMask"/>)
    /// to memorize which parts of the whole grid 
    /// belong to the subgrid.
    /// </summary>
    [DebuggerDisplay("{GetSummary()}")]
    public abstract class ExecutionMask : IEnumerable<Chunk> {

        /// <summary>
        /// Main storage entity of this class. Encoding: 
        /// <list type="bullet">
        ///  <item>
        ///   entry <em>i</em> is positive number <em>v</em>:
        ///   A <see cref="Chunk"/> with <see cref="Chunk.i0"/>==<em>v</em>-1 and
        ///   <see cref="Chunk.Len"/>==1;
        ///  </item>
        ///  <item>
        ///   entry <em>i</em> is negative number <em>v</em>:
        ///   A <see cref="Chunk"/> with <see cref="Chunk.i0"/>== -<em>v</em>-1 and
        ///   <see cref="Chunk.Len"/>==<see cref="Sequence"/>[<em>i</em>+1];
        ///  </item>
        /// </list>
        /// </summary>
        private int[] Sequence;

        /// <summary>
        /// Cache for the bit mask corresponding to the mask defined by the
        /// sequence in <see cref="Sequence"/>
        /// </summary>
        private BitArray m_BitMask = null;

        /// <summary>
        /// <see cref="IMax"/>
        /// </summary>
        private int m_IMax = 0;

        ///// <summary>
        ///// <see cref="Qtype"/>
        ///// </summary>
        //private MaskType m_qtype;

        IGridData m_GridData;

        /// <summary>
        /// the grid that this mask is associated with (necessary for methods like <see cref="Complement()"/>, ...)
        /// </summary>
        public IGridData GridData {
            get {
                return m_GridData;
            }
        }

        /// <summary>
        /// Builds an execution mask from a bit array. If an entry is set to
        /// true in the bit array, it indicates that an element should be part
        /// of the execution mask.
        /// </summary>
        /// <param name="Mask">The mask as <see cref="BitArray"/></param>
        /// <param name="grddat">
        /// the grid that this mask will be associated with;
        /// </param>
        protected ExecutionMask(IGridData grddat, BitArray Mask) {
            if (grddat == null)
                throw new ArgumentNullException();

            List<int> _Sequence = new List<int>();
            //            m_qtype = type;
            m_GridData = grddat;

            int Lmax = GetTotalNumberOfElements(this.m_GridData);
            if (Mask.Count > Lmax)
                throw new ArgumentException("length of mask must be smaller or equal to number of cells/edges;", "Mask");

            int N = Mask.Count;

            Chunk c;
            c.i0 = -1;
            c.Len = 0;
            for (int n = 0; n < N; n++) {
                bool mn = Mask[n];

                if (mn == true) {
                    if (c.i0 < 0) {
                        c.i0 = n;
                    }
                    c.Len++;
                } else {
                    if (c.i0 >= 0) {
                        // close chunk
                        PushChunk(_Sequence, c);
                        c.i0 = -1;
                        c.Len = 0;
                    }
                }
            }
            if (c.i0 >= 0) {
                // close chunk
                PushChunk(_Sequence, c);
            }

            Sequence = _Sequence.ToArray();
            m_BitMask = Mask;

            CompIMax();
        }

        /// <summary>
        /// For those users who know what they are doing, here 
        /// a fast version without any 'compilation'; The parameters are not
        /// checked;
        /// </summary>
        /// <param name="_Sequence">
        /// encoding of the quadrature execution mask: 
        /// <list type="bullet">
        ///  <item>
        ///   entry <em>i</em> is positive number <em>v</em>:
        ///   A <see cref="Chunk"/> with <see cref="Chunk.i0"/>==<em>v</em>-1 and
        ///   <see cref="Chunk.Len"/>==1;
        ///  </item>
        ///  <item>
        ///   entry <em>i</em> is negative number <em>v</em>:
        ///   A <see cref="Chunk"/> with <see cref="Chunk.i0"/>== -<em>v</em>-1 and
        ///   <see cref="Chunk.Len"/>==<see cref="Sequence"/>[<em>i</em>+1];
        ///  </item>
        /// </list>
        /// </param>
        /// <param name="grddat">
        /// the grid that this mask will be associated with;
        /// </param>
        protected ExecutionMask(IGridData grddat, int[] _Sequence) {
            if (grddat == null)
                throw new ArgumentNullException();
            this.m_GridData = grddat;
            Sequence = _Sequence;
            CompIMax();
        }

        /// <summary>
        /// Compiles an quadrature execution mask from a set of indices
        /// </summary>
        /// <param name="Indices">
        /// A list of indices, duplicates are ignored
        /// </param>
        /// <param name="grddat">
        /// the grid that this mask will be associated with;
        /// </param>
        protected ExecutionMask(IGridData grddat, IEnumerable<int> Indices)
            : this(grddat, FromIndEnum(Indices)) {
        }

        /// <summary>
        /// returns a bitmask which marks the same items as this object
        /// </summary>
        /// <remarks>
        /// see also <see cref="CellMask.GetBitMaskWithExternal"/>
        /// </remarks>
        public BitArray GetBitMask() {
            if (m_BitMask == null) {
                int I = GetTotalNumberOfElements(m_GridData);
                m_BitMask = new BitArray(I, false);
                foreach (Chunk c in this) {
                    for (int i = 0; i < c.Len; i++) {
                        m_BitMask[i + c.i0] = true;
                    }
                }
            } else {
                Debug.Assert(m_BitMask.Length == GetTotalNumberOfElements(m_GridData));
            }
            Debug.Assert(m_BitMask.Length == GetTotalNumberOfElements(m_GridData));
            return m_BitMask;
        }

        /// <summary>
        /// Creates an execution mask which only contains elements that are
        /// included in this mask and in the given mask
        /// <paramref name="otherMask"/>.
        /// </summary>
        /// <param name="otherMask">
        /// The mask to intersect this mask with
        /// </param>
        /// <returns>
        /// A new execution mask representing
        /// \f$ \mathrm{this} \cap \mathrm{otherMask}\f$ 
        /// </returns>
        public T Intersect<T>(T otherMask) where T : ExecutionMask {
            if (this.GetType() != otherMask.GetType()) {
                throw new ArgumentException("Illegal mix of quadrature types: " + this.GetType().Name + " and " + otherMask.GetType().Name, "otherMask");
            }
            if (!Object.ReferenceEquals(this.m_GridData, otherMask.m_GridData))
                throw new ArgumentException("execution mask are associated with different grids.", "otherMask");

            BitArray array = GetBitMask();
            BitArray otherArray = otherMask.GetBitMask();
            return (T)CreateInstance(m_GridData, ((BitArray)array.Clone()).And(otherArray));
        }

        /// <summary>
        /// similar to constructor
        /// </summary>
        abstract protected ExecutionMask CreateInstance(IGridData grdDat, BitArray mask);


        /// <summary>
        /// Creates an execution mask which only contains elements that are
        /// included in this mask but not in <paramref name="otherMask"/>,
        /// </summary>
        /// <param name="otherMask">
        /// The mask containing the elements to exclude
        /// </param>
        /// <returns>
        /// A new execution mask representing
        /// \f$ \mathrm{this} \setminus \mathrm{otherMask}\f$ 
        /// </returns>
        public T Except<T>(T otherMask) where T : ExecutionMask {
            if (this.GetType() != otherMask.GetType()) {
                throw new ArgumentException("Illegal mix of quadrature types: " + this.GetType().Name + " and " + otherMask.GetType().Name, "otherMask");
            }
            if (!object.ReferenceEquals(otherMask.m_GridData, this.m_GridData))
                throw new ArgumentException("masks cannot be assigned to different grids.");

            BitArray array = GetBitMask();
            BitArray otherArray = otherMask.GetBitMask();
            return (T)CreateInstance(m_GridData, ((BitArray)array.Clone()).And(((BitArray)otherArray.Clone()).Not()));
        }

        /// <summary>
        /// Creates an execution mask that is the union of two masks;
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="maskA">1st operand</param>
        /// <param name="maskB">2nd operand</param>
        /// <returns>
        /// the union (logical OR) of <paramref name="maskA"/> and <paramref name="maskB"/>
        /// </returns>
        static public T Union<T>(T maskA, T maskB) where T : ExecutionMask {
            if (maskA.GetType() != maskB.GetType())
                throw new ArgumentException("Unable to operate on different types of Illegal mix of quadrature types: " + maskA.GetType().Name + " and " + maskB.GetType().Name);
            if (!object.ReferenceEquals(maskA.m_GridData, maskB.m_GridData))
                throw new ArgumentException("masks cannot be assigned to different grids.");

            BitArray a = maskA.GetBitMask();
            BitArray b = maskB.GetBitMask();


            return (T)maskA.CreateInstance(maskA.m_GridData, ((BitArray)a.Clone()).Or(b));
        }

        /// <summary>
        /// Creates an execution mask that is the intersection of two masks;
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="maskA">1st operand</param>
        /// <param name="maskB">2nd operand</param>
        /// <returns>
        /// the intersection (logical OR) of <paramref name="maskA"/> and <paramref name="maskB"/>
        /// </returns>
        static public T Intersect<T>(T maskA, T maskB) where T : ExecutionMask {
            if (maskA.GetType() != maskB.GetType())
                throw new ArgumentException("Unable to operate on different types of Illegal mix of quadrature types: " + maskA.GetType().Name + " and " + maskB.GetType().Name);
            if (!object.ReferenceEquals(maskA.m_GridData, maskB.m_GridData))
                throw new ArgumentException("masks cannot be assigned to different grids.");

            BitArray a = maskA.GetBitMask();
            BitArray b = maskB.GetBitMask();

            return (T)maskA.CreateInstance(maskA.m_GridData, ((BitArray)a.Clone()).And(b));
        }

        /// <summary>
        /// Creates an execution mask which only contains elements that are
        /// included in this mask OR in the given mask
        /// <paramref name="otherMask"/>.
        /// </summary>
        /// <param name="otherMask">
        /// The mask to union this mask with
        /// </param>
        /// <returns>
        /// A new execution mask representing
        /// \f$ \mathrm{this} \cup \mathrm{otherMask}\f$ 
        /// </returns>
        public T Union<T>(T otherMask) where T : ExecutionMask {
            if (this.GetType() != otherMask.GetType()) {
                throw new ArgumentException("Illegal mix of quadrature types: " + this.GetType().Name + " and " + otherMask.GetType().Name, "otherMask");
            }
            if (!Object.ReferenceEquals(this.m_GridData, otherMask.m_GridData))
                throw new ArgumentException("execution mask are associated with different grids.", "otherMask");

            BitArray array = GetBitMask();
            BitArray otherArray = otherMask.GetBitMask();
            return (T)CreateInstance(m_GridData, ((BitArray)array.Clone()).Or(otherArray));
        }

        /// <summary>
        /// The highest used element (cell or edge) index in this execution
        /// mask; For an empty mask, it is -1;
        /// </summary>
        public int IMax {
            get {
                return m_IMax;
            }
        }


        int m_NoOfItems = -1;

        /// <summary>
        /// number of mask items (cells or edges), locally on this MPI process
        /// </summary>
        public int NoOfItemsLocally {
            get {
                if (m_NoOfItems < 0) {
                    m_NoOfItems = 0;
                    foreach (Chunk s in this) {
                        m_NoOfItems += s.Len;
                    }
                }
                return m_NoOfItems;
            }
        }

        ///// <summary>
        ///// number of mask items (cells or edges), over all MPI processes
        ///// </summary>
        //public int NoOfItemsGlobally {
        //    get {
        //        int loc = NoOfItemsLocally;
        //        ilPSP.MPICollectiveWatchDog.Watch(csMPI.Raw._COMM.WORLD);
        //        unsafe {
        //            int glob = int.MinValue;
        //            csMPI.Raw.Allreduce(((IntPtr)(&loc)), ((IntPtr)(&glob)), 1, csMPI.Raw._DATATYPE.INT, csMPI.Raw._OP.SUM, csMPI.Raw._COMM.WORLD);
        //            return glob;
        //        }
        //    }
        //}



        /// <summary>
        /// Compares two masks according to the masked
        /// elements
        /// </summary>
        /// <param name="obj">The object to compare this object to</param>
        /// <returns>
        /// True, if the given object represents a mask of the same type (cell/edge)
        /// containing exactly the same cells/edges as this object.
        /// </returns>
        public override bool Equals(object obj) {
            ExecutionMask o = obj as ExecutionMask;
            if (o == null) {
                return false;
            }

            if (o.GetType() != this.GetType())
                return false;

            if (o.m_IMax != this.m_IMax) {
                return false;
            }

            return Enumerable.SequenceEqual(o.Sequence, this.Sequence);
        }

        /// <summary>
        /// <see cref="Object.GetHashCode"/>
        /// </summary>
        /// <returns><see cref="Object.GetHashCode"/></returns>
        public override int GetHashCode() {
            int hash = this.Sequence.Length;
            foreach (var chunk in this) {
                hash ^= chunk.GetHashCode();
            }
            return hash;
        }

        /// <summary>
        /// Computes <see cref="IMax"/>
        /// </summary>
        private void CompIMax() {
            if (Sequence.Length >= 2) {
                int sLm = Sequence[Sequence.Length - 1];
                int sLmm = Sequence[Sequence.Length - 2];

                if (sLmm < 0) {
                    m_IMax = -sLmm + sLm - 1;
                } else {
                    m_IMax = sLm;
                }
            } else if (Sequence.Length >= 1) {
                m_IMax = Sequence[0];
            } else {
                // empty sequence
                m_IMax = 0;
            }
            m_IMax--;

            if (m_IMax >= this.GetTotalNumberOfElements(this.m_GridData))
                throw new ArgumentException("Provided data exceeds range of local number of elements.");
        }

        /// <summary>
        /// Pushes chunk <paramref name="c"/> onto <paramref name="seq"/> by
        /// encoding it according to the entries of <see cref="Sequence"/>
        /// </summary>
        /// <param name="seq">The list the chunk will be pushed onto</param>
        /// <param name="c">The chunk to be pushed</param>
        private static void PushChunk(List<int> seq, Chunk c) {
            c.i0++;
            if (c.Len > 1) {
                c.i0 *= -1;
            }
            seq.Add(c.i0);
            if (c.Len > 1) {
                seq.Add(c.Len);
            }
        }

        /// <summary>
        /// Transforms a list of chunks into a sequence.
        /// </summary>
        /// <param name="parts">The chunks</param>
        protected static int[] FromChunkEnum(IEnumerable<Chunk> parts) {

            int[] R = new int[parts.Count() * 2];
            int C = 0;

            Chunk prev = default(Chunk);

            var enu = parts.GetEnumerator();
            foreach (var chk in parts) {
                if (chk.i0 < 0) {
                    throw new IndexOutOfRangeException("negative index is not allowed");
                }
                if (chk.Len <= 0) {
                    throw new ArgumentException("length of a chunk must be greater or equal to 1.");
                }
                Chunk _chk = chk;

                if (C > 0 && prev.JE == chk.i0) {
                    // chunk merging
                    _chk.i0 = prev.i0;
                    _chk.Len = chk.Len + prev.Len;
                    C--;
                    if (prev.Len > 1)
                        C--;
                }

                if (_chk.Len > 1) {
                    // multiple-element-chunk
                    R[C] = -(_chk.i0 + 1);
                    C++;
                    R[C] = _chk.Len;
                    C++;
                } else {
                    // single-element chunk
                    R[C] = _chk.i0 + 1;
                    C++;
                }
                prev = _chk;
            }


            if (C < R.Length)
                Array.Resize(ref R, C);
            return R;
        }

        /// <summary>
        /// Constructs the <see cref="BitArray"/> representation of the given
        /// list of indices
        /// </summary>
        /// <param name="Indices">
        /// A list of entries that are contained in the mask
        /// </param>
        /// <returns>
        /// A <see cref="BitArray"/> representation of the mask defined by
        /// <paramref name="Indices"/>
        /// </returns>
        private static BitArray FromIndEnum(IEnumerable<int> Indices) {
            int iMax = 0;
            foreach (int i in Indices) {
                if (i < 0) {
                    throw new IndexOutOfRangeException("negative index is not allowed");
                }
                iMax = Math.Max(i, iMax);
            }

            BitArray b = new BitArray(iMax + 1);
            foreach (int i in Indices) {
                b[i] = true;
            }
            return b;
        }

        /// <summary>
        /// Complement of this execution mask (all elements that are not in this mask);
        /// </summary>
        public T Complement<T>() where T : ExecutionMask {
            int I = GetTotalNumberOfElements(this.m_GridData);

            BitArray ba = ((BitArray)this.GetBitMask().Clone()).Not();
            return (T)CreateInstance(m_GridData, ba);
        }

        /// <summary>
        /// static version of <see cref="Complement()"/>
        /// </summary>
        public static T Complement<T>(T A) where T : ExecutionMask {
            return A.Complement<T>();
        }

        /// <summary>
        /// true if this mask contains no items
        /// </summary>
        public bool IsEmpty {
            get {
                foreach (var c in this) {
                    if (c.Len > 0)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// true if this mask is a subset of <paramref name="o"/>
        /// </summary>
        public bool IsSubMaskOf(ExecutionMask o) {
            if (this.GetType() != o.GetType())
                throw new ArgumentException("unable to compare two masks of different type: " + this.GetType().FullName + ", " + this.GetType().FullName + ";");
            if (!object.ReferenceEquals(this.GridData, o.GridData))
                throw new ArgumentException("masks live on different grids.");

            if (this.NoOfItemsLocally > o.NoOfItemsLocally)
                return false;

            BitArray omask = o.GetBitMask();
            foreach (var c in this) {
                for (int i = 0; i < c.Len; i++) {
                    int j = i + c.i0;
                    if (!omask[j])
                        return false;
                }
            }

            // all tests passed positive
            return true;
        }

        /// <summary>
        /// Retrieves the total number of elements in
        /// <paramref name="gridData"/> which is either
        /// <see cref="BoSSS.Foundation.Grid.GridData.EdgeData.Count"/> or
        /// <see cref="BoSSS.Foundation.Grid.GridData.CellData.NoOfLocalUpdatedCells"/>.
        /// </summary>
        /// <param name="gridData">
        /// The grid data object referring to the grid elements that should be
        /// masked by an execution mask
        /// </param>
        /// <returns>
        /// </returns>
        protected abstract int GetTotalNumberOfElements(IGridData gridData);

        /// <summary>
        /// used by <see cref="ToTxtFile"/>
        /// </summary>
        /// <param name="CoordGlobal">
        /// edge or cell center, in global/physical coordinates
        /// </param>
        /// <param name="LogicalItemIndex">
        /// Logical cell or edge index, see <see cref="ILogicalCellData"/> resp. <see cref="ILogicalEdgeData"/>.
        /// </param>
        /// <param name="GeomItemIndex">
        /// Geometrical cell or edge index, see <see cref="IGeometricalCellsData"/> resp. <see cref="IGeometricalEdgeData"/>.
        /// </param>
        /// <returns>
        /// some date
        /// </returns>
        public delegate double ItemInfo(double[] CoordGlobal, int LogicalItemIndex, int GeomItemIndex);

        /// <summary>
        /// Serializes all elements of this mask (identified by global/physical coordinates) to a CSV format and saves the
        /// result in a file with the given <paramref name="fileName"/>.
        /// </summary>
        /// <param name="fileName">
        /// The name of the file to contain the results.
        /// </param>
        /// <param name="infoFunc">
        /// Optional; provides information that a user may want to append to the list of coordinates, for each item.
        /// </param>
        /// <param name="WriteHeader">
        /// if true, the first line of the file will contain column names
        /// </param>
        public abstract void SaveToTextFile(string fileName, bool WriteHeader = true, params ItemInfo[] infoFunc);

        /// <summary>
        /// Used for displaying a summary of this mask in the debugger
        /// </summary>
        /// <returns></returns>
        public string GetSummary() {
            string summary = "{";
            string separator = " ";
            foreach (Chunk chunk in this) {
                summary += separator;

                if (chunk.Len == 1) {
                    summary += chunk.i0;
                } else {
                    summary += chunk.i0 + "-" + (chunk.JE - 1);
                }

                separator = ", ";
            }
            summary += " }";

            return summary;
        }

        /// <summary>
        /// Checks whether the given <paramref name="element"/> is contained
        /// in this mask
        /// </summary>
        /// <param name="element">
        /// The element in question
        /// </param>
        /// <returns>
        /// True, if <paramref name="element"/> is contained in this execution
        /// mask; false otherwise.
        /// </returns>
        public bool Contains(int element) {
            foreach (Chunk chunk in this) {
                if (element >= chunk.i0 && element < chunk.JE) {
                    return true;
                }
            }

            return false;
        }

        #region IEnumerable<Chunk> Members

        /// <summary>
        /// Chunk-wise enumeration
        /// </summary>
        /// <returns>A new <see cref="ChunkEnumerator"/></returns>
        public IEnumerator<Chunk> GetEnumerator() {
            return new ChunkEnumerator(this);
        }

        #endregion

        #region IEnumerable Members

        /// <summary>
        /// Chunk-wise enumeration
        /// </summary>
        /// <returns>a new <see cref="ChunkEnumerator"/></returns>
        IEnumerator IEnumerable.GetEnumerator() {
            return new ChunkEnumerator(this);
        }

        #endregion

        /// <summary>
        /// Enumerates over the execution mask, one <see cref="Chunk"/> at a
        /// time
        /// </summary>
        class ChunkEnumerator : IEnumerator<Chunk> {

            /// <summary>
            /// The creator of this enumerator
            /// </summary>
            private ExecutionMask owner;

            /// <summary>
            /// Counter into <see cref="owner"/>.<see cref="ExecutionMask.Sequence"/> 
            /// </summary>
            private int cnt = 0;

            /// <summary>
            /// <see cref="Current"/>
            /// </summary>
            private Chunk curr;

            /// <summary>
            /// Creates a new enumerator
            /// </summary>
            /// <param name="__owner">The creator of this enumerator</param>
            public ChunkEnumerator(ExecutionMask __owner) {
                owner = __owner;
                curr.i0 = -1;
                curr.Len = -1;
            }

            #region IEnumerator<Chunk> Members

            /// <summary>
            /// The current chunk
            /// </summary>
            public Chunk Current {
                get {
                    return curr;
                }
            }

            #endregion

            #region IDisposable Members

            /// <summary>
            /// Empty
            /// </summary>
            public void Dispose() {
            }

            #endregion

            #region IEnumerator Members

            /// <summary>
            /// Returns the current chunk
            /// </summary>
            object IEnumerator.Current {
                get {
                    return curr;
                }
            }

            /// <summary>
            /// Advances to the next chunk in <see cref="owner"/>.Sequence
            /// </summary>
            /// <returns></returns>
            public bool MoveNext() {
                if (cnt >= owner.Sequence.Length) {
                    return false;
                }

                curr.i0 = owner.Sequence[cnt];
                if (curr.i0 < 0) {
                    curr.i0 *= -1;
                    cnt++;
                    curr.Len = owner.Sequence[cnt];
                } else {
                    curr.Len = 1;
                }
                curr.i0--;
                cnt++;
                return true;
            }

            /// <summary>
            /// Returns to the first chunk
            /// </summary>
            public void Reset() {
                cnt = 0;
                curr.i0 = -1;
                curr.Len = -1;
            }

            #endregion
        }

        /// <summary>
        /// An enumerator which contains all items/indices in this mask;
        /// </summary>
        public IEnumerator<int> GetItemEnumerator() {
            var r = new ItemEnumerator() { ChunkEnum = this.GetEnumerator() };
            return r;
        }

        class ItemEnumerator : IEnumerator<int> {

            internal IEnumerator<Chunk> ChunkEnum;

            public int Current {
                get {
                    return Item;
                }
            }

            public void Dispose() {
                ChunkEnum.Dispose();
                Item = int.MaxValue;
            }

            object IEnumerator.Current {
                get {
                    return Item;
                }
            }

            int Item = -1;
            Chunk CurrentChunk;

            public bool MoveNext() {
                if (Item < 0) {
                    bool b = ChunkEnum.MoveNext();
                    if (b == false)
                        return false;
                    CurrentChunk = ChunkEnum.Current;
                    Item = CurrentChunk.i0 - 1;
                }

                while (true) {
                    if (Item < CurrentChunk.JE - 1) {
                        Item++;
                        return true;
                    }

                    bool b = ChunkEnum.MoveNext();
                    if (b == false)
                        return false;
                    CurrentChunk = ChunkEnum.Current;
                    Item = CurrentChunk.i0 - 1;
                }
            }

            public void Reset() {
                ChunkEnum.Reset();
                Item = -1;
            }
        }

        /// <summary>
        /// An enumerable which contains all items/indices in this mask;
        /// </summary>
        public IEnumerable<int> ItemEnum {
            get {
                var r = new ItemEnumerable() { owner = this };
                return r;
            }
        }

        class ItemEnumerable : IEnumerable<int> {

            internal ExecutionMask owner;

            public IEnumerator<int> GetEnumerator() {
                return owner.GetItemEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return this.GetEnumerator();
            }
        }

    }
}
