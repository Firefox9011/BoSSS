/*BHEADER**********************************************************************
 * Copyright (c) 2008,  Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * This file is part of HYPRE.  See file COPYRIGHT for details.
 *
 * HYPRE is free software; you can redistribute it and/or modify it under the
 * terms of the GNU Lesser General Public License (as published by the Free
 * Software Foundation) version 2.1 dated February 1999.
 *
 * $Revision: 1.12 $
 ***********************************************************************EHEADER*/





/******************************************************************************
 *
 * Header info for CSR Block Matrix data structures
 *
 * Note: this matrix currently uses 0-based indexing.
 * Note: everything is in terms of blocks (ie. num_rows is the number
 *       of block rows)
 *
 *****************************************************************************/


#ifndef hypre_CSR_BLOCK_MATRIX_HEADER
#define hypre_CSR_BLOCK_MATRIX_HEADER

#include "seq_mv.h"
#include "_hypre_utilities.h"
                                                                                                               
#ifdef __cplusplus
extern "C" {
#endif
                                                                                                               

/*--------------------------------------------------------------------------
 * CSR Block Matrix
 *--------------------------------------------------------------------------*/

typedef struct
{
  double	        *data;
  int                   *i;
  int                   *j;
  int                   block_size;
  int     		num_rows;
  int     		num_cols;
  int                   num_nonzeros;

  int                   owns_data;

} hypre_CSRBlockMatrix;

/*--------------------------------------------------------------------------
 * Accessor functions for the CSR Block Matrix structure
 *--------------------------------------------------------------------------*/

#define hypre_CSRBlockMatrixData(matrix)         ((matrix) -> data)
#define hypre_CSRBlockMatrixI(matrix)            ((matrix) -> i)
#define hypre_CSRBlockMatrixJ(matrix)            ((matrix) -> j)
#define hypre_CSRBlockMatrixBlockSize(matrix)    ((matrix) -> block_size)
#define hypre_CSRBlockMatrixNumRows(matrix)      ((matrix) -> num_rows)
#define hypre_CSRBlockMatrixNumCols(matrix)      ((matrix) -> num_cols)
#define hypre_CSRBlockMatrixNumNonzeros(matrix)  ((matrix) -> num_nonzeros)
#define hypre_CSRBlockMatrixOwnsData(matrix)     ((matrix) -> owns_data)

/*--------------------------------------------------------------------------
 * other functions for the CSR Block Matrix structure
 *--------------------------------------------------------------------------*/

hypre_CSRBlockMatrix 
      *hypre_CSRBlockMatrixCreate(int, int, int, int);
int hypre_CSRBlockMatrixDestroy(hypre_CSRBlockMatrix *);
int hypre_CSRBlockMatrixInitialize(hypre_CSRBlockMatrix *);
int hypre_CSRBlockMatrixSetDataOwner(hypre_CSRBlockMatrix *, int);
hypre_CSRMatrix 
      *hypre_CSRBlockMatrixCompress(hypre_CSRBlockMatrix *);
hypre_CSRMatrix 
      *hypre_CSRBlockMatrixConvertToCSRMatrix(hypre_CSRBlockMatrix *);
hypre_CSRBlockMatrix
      *hypre_CSRBlockMatrixConvertFromCSRMatrix(hypre_CSRMatrix *, int);
int hypre_CSRBlockMatrixBlockAdd(double *, double *, double*, int);

int hypre_CSRBlockMatrixBlockMultAdd(double *, double *, double, double *, int);
int hypre_CSRBlockMatrixBlockMultAddDiag(double *, double *, double, double *, int);
int
hypre_CSRBlockMatrixBlockMultAddDiag2(double* i1, double* i2, double beta, 
                                      double* o, int block_size);
int
hypre_CSRBlockMatrixBlockMultAddDiag3(double* i1, double* i2, double beta, 
                                      double* o, int block_size);
   

int hypre_CSRBlockMatrixBlockInvMult(double *, double *, double *, int);
int hypre_CSRBlockMatrixBlockInvMultDiag(double *, double *, double *, int);

int
hypre_CSRBlockMatrixBlockInvMultDiag2(double* i1, double* i2, double* o, int block_size);
   
int
hypre_CSRBlockMatrixBlockInvMultDiag3(double* i1, double* i2, double* o, int block_size);
   



int hypre_CSRBlockMatrixBlockMultInv(double *, double *, double *, int);
int hypre_CSRBlockMatrixBlockTranspose(double *, double *, int);

int hypre_CSRBlockMatrixTranspose(hypre_CSRBlockMatrix *A,
                                  hypre_CSRBlockMatrix **AT, int data);

int hypre_CSRBlockMatrixBlockCopyData(double*, double*, double, int);
int hypre_CSRBlockMatrixBlockCopyDataDiag(double*, double*, double, int);

int hypre_CSRBlockMatrixBlockAddAccumulate(double*, double*, int);
int hypre_CSRBlockMatrixBlockAddAccumulateDiag(double* i1, double* o, int block_size);
   


int
hypre_CSRBlockMatrixMatvec(double alpha, hypre_CSRBlockMatrix *A,
                           hypre_Vector *x, double beta, hypre_Vector *y);
   

int
hypre_CSRBlockMatrixMatvecT( double alpha, hypre_CSRBlockMatrix *A, hypre_Vector  *x,
                             double beta, hypre_Vector *y );

int
hypre_CSRBlockMatrixBlockInvMatvec(double* mat, double* v, 
                                   double* ov, int block_size);
   
int 
hypre_CSRBlockMatrixBlockMatvec(double alpha, double* mat, double* v, double beta, 
                                double* ov, int block_size);
   

int hypre_CSRBlockMatrixBlockNorm(int norm_type, double* data, double* out, int block_size);
   
int hypre_CSRBlockMatrixBlockSetScalar(double* o, double beta, int block_size);
   
int hypre_CSRBlockMatrixComputeSign(double *i1, double *o, int block_size);
int hypre_CSRBlockMatrixBlockAddAccumulateDiagCheckSign(double* i1, double* o, int block_size, double *sign);
int hypre_CSRBlockMatrixBlockMultAddDiagCheckSign(double* i1, double* i2, double beta, 
                                              double* o, int block_size, double *sign);
   




#ifdef __cplusplus
}
#endif
#endif
