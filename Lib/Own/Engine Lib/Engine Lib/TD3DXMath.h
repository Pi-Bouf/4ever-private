#pragma once

// Replacement for d3dx9math.h: the same types and functions, so the call
// sites do not change, but no d3dx9.lib code behind them (the bgfx and Linux
// ports have no D3DX). Include it before d3dx9.h; it defines the d3dx9math.h
// include guard so the rest of d3dx9 still compiles against these types.
// Only what the client uses is here.
#define __D3DX9MATH_H__

#include <math.h>
#include <string.h>

#define D3DX_PI						((FLOAT) 3.141592654f)
#define D3DX_1BYPI					((FLOAT) 0.318309886f)

#define D3DXToRadian( degree )		((degree) * (D3DX_PI / 180.0f))
#define D3DXToDegree( radian )		((radian) * (180.0f / D3DX_PI))


typedef struct D3DXVECTOR2
{
	FLOAT x, y;

	D3DXVECTOR2() {}
	D3DXVECTOR2( const FLOAT *pf) : x(pf[0]), y(pf[1]) {}
	D3DXVECTOR2( FLOAT fx, FLOAT fy) : x(fx), y(fy) {}

	operator FLOAT* () { return &x; }
	operator const FLOAT* () const { return &x; }

	D3DXVECTOR2& operator += ( const D3DXVECTOR2& v) { x += v.x; y += v.y; return *this; }
	D3DXVECTOR2& operator -= ( const D3DXVECTOR2& v) { x -= v.x; y -= v.y; return *this; }
	D3DXVECTOR2& operator *= ( FLOAT f) { x *= f; y *= f; return *this; }
	D3DXVECTOR2& operator /= ( FLOAT f) { FLOAT inv = 1.0f / f; x *= inv; y *= inv; return *this; }

	D3DXVECTOR2 operator + () const { return *this; }
	D3DXVECTOR2 operator - () const { return D3DXVECTOR2( -x, -y); }

	D3DXVECTOR2 operator + ( const D3DXVECTOR2& v) const { return D3DXVECTOR2( x + v.x, y + v.y); }
	D3DXVECTOR2 operator - ( const D3DXVECTOR2& v) const { return D3DXVECTOR2( x - v.x, y - v.y); }
	D3DXVECTOR2 operator * ( FLOAT f) const { return D3DXVECTOR2( x * f, y * f); }
	D3DXVECTOR2 operator / ( FLOAT f) const { FLOAT inv = 1.0f / f; return D3DXVECTOR2( x * inv, y * inv); }

	friend D3DXVECTOR2 operator * ( FLOAT f, const D3DXVECTOR2& v) { return D3DXVECTOR2( f * v.x, f * v.y); }

	BOOL operator == ( const D3DXVECTOR2& v) const { return x == v.x && y == v.y; }
	BOOL operator != ( const D3DXVECTOR2& v) const { return x != v.x || y != v.y; }
} D3DXVECTOR2, *LPD3DXVECTOR2;


typedef struct D3DXVECTOR3 : public D3DVECTOR
{
	D3DXVECTOR3() {}
	D3DXVECTOR3( const FLOAT *pf) { x = pf[0]; y = pf[1]; z = pf[2]; }
	D3DXVECTOR3( const D3DVECTOR& v) { x = v.x; y = v.y; z = v.z; }
	D3DXVECTOR3( FLOAT fx, FLOAT fy, FLOAT fz) { x = fx; y = fy; z = fz; }

	operator FLOAT* () { return &x; }
	operator const FLOAT* () const { return &x; }

	D3DXVECTOR3& operator += ( const D3DXVECTOR3& v) { x += v.x; y += v.y; z += v.z; return *this; }
	D3DXVECTOR3& operator -= ( const D3DXVECTOR3& v) { x -= v.x; y -= v.y; z -= v.z; return *this; }
	D3DXVECTOR3& operator *= ( FLOAT f) { x *= f; y *= f; z *= f; return *this; }
	D3DXVECTOR3& operator /= ( FLOAT f) { FLOAT inv = 1.0f / f; x *= inv; y *= inv; z *= inv; return *this; }

	D3DXVECTOR3 operator + () const { return *this; }
	D3DXVECTOR3 operator - () const { return D3DXVECTOR3( -x, -y, -z); }

	D3DXVECTOR3 operator + ( const D3DXVECTOR3& v) const { return D3DXVECTOR3( x + v.x, y + v.y, z + v.z); }
	D3DXVECTOR3 operator - ( const D3DXVECTOR3& v) const { return D3DXVECTOR3( x - v.x, y - v.y, z - v.z); }
	D3DXVECTOR3 operator * ( FLOAT f) const { return D3DXVECTOR3( x * f, y * f, z * f); }
	D3DXVECTOR3 operator / ( FLOAT f) const { FLOAT inv = 1.0f / f; return D3DXVECTOR3( x * inv, y * inv, z * inv); }

	friend D3DXVECTOR3 operator * ( FLOAT f, const D3DXVECTOR3& v) { return D3DXVECTOR3( f * v.x, f * v.y, f * v.z); }

	BOOL operator == ( const D3DXVECTOR3& v) const { return x == v.x && y == v.y && z == v.z; }
	BOOL operator != ( const D3DXVECTOR3& v) const { return x != v.x || y != v.y || z != v.z; }
} D3DXVECTOR3, *LPD3DXVECTOR3;


typedef struct D3DXVECTOR4
{
	FLOAT x, y, z, w;

	D3DXVECTOR4() {}
	D3DXVECTOR4( const FLOAT *pf) : x(pf[0]), y(pf[1]), z(pf[2]), w(pf[3]) {}
	D3DXVECTOR4( const D3DVECTOR& v, FLOAT fw) : x(v.x), y(v.y), z(v.z), w(fw) {}
	D3DXVECTOR4( FLOAT fx, FLOAT fy, FLOAT fz, FLOAT fw) : x(fx), y(fy), z(fz), w(fw) {}

	operator FLOAT* () { return &x; }
	operator const FLOAT* () const { return &x; }

	D3DXVECTOR4& operator += ( const D3DXVECTOR4& v) { x += v.x; y += v.y; z += v.z; w += v.w; return *this; }
	D3DXVECTOR4& operator -= ( const D3DXVECTOR4& v) { x -= v.x; y -= v.y; z -= v.z; w -= v.w; return *this; }
	D3DXVECTOR4& operator *= ( FLOAT f) { x *= f; y *= f; z *= f; w *= f; return *this; }
	D3DXVECTOR4& operator /= ( FLOAT f) { FLOAT inv = 1.0f / f; x *= inv; y *= inv; z *= inv; w *= inv; return *this; }

	D3DXVECTOR4 operator + () const { return *this; }
	D3DXVECTOR4 operator - () const { return D3DXVECTOR4( -x, -y, -z, -w); }

	D3DXVECTOR4 operator + ( const D3DXVECTOR4& v) const { return D3DXVECTOR4( x + v.x, y + v.y, z + v.z, w + v.w); }
	D3DXVECTOR4 operator - ( const D3DXVECTOR4& v) const { return D3DXVECTOR4( x - v.x, y - v.y, z - v.z, w - v.w); }
	D3DXVECTOR4 operator * ( FLOAT f) const { return D3DXVECTOR4( x * f, y * f, z * f, w * f); }
	D3DXVECTOR4 operator / ( FLOAT f) const { FLOAT inv = 1.0f / f; return D3DXVECTOR4( x * inv, y * inv, z * inv, w * inv); }

	friend D3DXVECTOR4 operator * ( FLOAT f, const D3DXVECTOR4& v) { return D3DXVECTOR4( f * v.x, f * v.y, f * v.z, f * v.w); }

	BOOL operator == ( const D3DXVECTOR4& v) const { return x == v.x && y == v.y && z == v.z && w == v.w; }
	BOOL operator != ( const D3DXVECTOR4& v) const { return x != v.x || y != v.y || z != v.z || w != v.w; }
} D3DXVECTOR4, *LPD3DXVECTOR4;


struct D3DXMATRIX;
D3DXMATRIX* D3DXMatrixMultiply( D3DXMATRIX *pOut, const D3DXMATRIX *pM1, const D3DXMATRIX *pM2);

typedef struct D3DXMATRIX : public D3DMATRIX
{
	D3DXMATRIX() {}
	D3DXMATRIX( const FLOAT *pf) { memcpy( &_11, pf, sizeof(D3DMATRIX)); }
	D3DXMATRIX( const D3DMATRIX& mat) { memcpy( &_11, &mat, sizeof(D3DMATRIX)); }
	D3DXMATRIX(
		FLOAT f11, FLOAT f12, FLOAT f13, FLOAT f14,
		FLOAT f21, FLOAT f22, FLOAT f23, FLOAT f24,
		FLOAT f31, FLOAT f32, FLOAT f33, FLOAT f34,
		FLOAT f41, FLOAT f42, FLOAT f43, FLOAT f44)
	{
		_11 = f11; _12 = f12; _13 = f13; _14 = f14;
		_21 = f21; _22 = f22; _23 = f23; _24 = f24;
		_31 = f31; _32 = f32; _33 = f33; _34 = f34;
		_41 = f41; _42 = f42; _43 = f43; _44 = f44;
	}

	FLOAT& operator () ( UINT nRow, UINT nCol) { return m[nRow][nCol]; }
	FLOAT operator () ( UINT nRow, UINT nCol) const { return m[nRow][nCol]; }

	operator FLOAT* () { return &_11; }
	operator const FLOAT* () const { return &_11; }

	D3DXMATRIX& operator *= ( const D3DXMATRIX& mat) { D3DXMatrixMultiply( this, this, &mat); return *this; }
	D3DXMATRIX& operator += ( const D3DXMATRIX& mat)
	{
		for( int i=0; i<16; i++)
			(&_11)[i] += (&mat._11)[i];

		return *this;
	}
	D3DXMATRIX& operator -= ( const D3DXMATRIX& mat)
	{
		for( int i=0; i<16; i++)
			(&_11)[i] -= (&mat._11)[i];

		return *this;
	}
	D3DXMATRIX& operator *= ( FLOAT f)
	{
		for( int i=0; i<16; i++)
			(&_11)[i] *= f;

		return *this;
	}
	D3DXMATRIX& operator /= ( FLOAT f) { return *this *= 1.0f / f; }

	D3DXMATRIX operator + () const { return *this; }
	D3DXMATRIX operator - () const { D3DXMATRIX r(*this); return r *= -1.0f; }

	D3DXMATRIX operator * ( const D3DXMATRIX& mat) const { D3DXMATRIX r; D3DXMatrixMultiply( &r, this, &mat); return r; }
	D3DXMATRIX operator + ( const D3DXMATRIX& mat) const { D3DXMATRIX r(*this); return r += mat; }
	D3DXMATRIX operator - ( const D3DXMATRIX& mat) const { D3DXMATRIX r(*this); return r -= mat; }
	D3DXMATRIX operator * ( FLOAT f) const { D3DXMATRIX r(*this); return r *= f; }
	D3DXMATRIX operator / ( FLOAT f) const { D3DXMATRIX r(*this); return r /= f; }

	friend D3DXMATRIX operator * ( FLOAT f, const D3DXMATRIX& mat) { D3DXMATRIX r(mat); return r *= f; }

	BOOL operator == ( const D3DXMATRIX& mat) const { return !memcmp( this, &mat, sizeof(D3DXMATRIX)); }
	BOOL operator != ( const D3DXMATRIX& mat) const { return memcmp( this, &mat, sizeof(D3DXMATRIX)) != 0; }
} D3DXMATRIX, *LPD3DXMATRIX;

typedef struct alignas(16) D3DXMATRIXA16 : public D3DXMATRIX
{
	D3DXMATRIXA16() {}
	D3DXMATRIXA16( const FLOAT *pf) : D3DXMATRIX(pf) {}
	D3DXMATRIXA16( const D3DMATRIX& mat) : D3DXMATRIX(mat) {}
	D3DXMATRIXA16(
		FLOAT f11, FLOAT f12, FLOAT f13, FLOAT f14,
		FLOAT f21, FLOAT f22, FLOAT f23, FLOAT f24,
		FLOAT f31, FLOAT f32, FLOAT f33, FLOAT f34,
		FLOAT f41, FLOAT f42, FLOAT f43, FLOAT f44) : D3DXMATRIX(
			f11, f12, f13, f14,
			f21, f22, f23, f24,
			f31, f32, f33, f34,
			f41, f42, f43, f44) {}

	D3DXMATRIXA16& operator = ( const D3DXMATRIX& mat) { memcpy( &_11, &mat, sizeof(D3DXMATRIX)); return *this; }
} D3DXMATRIXA16, *LPD3DXMATRIXA16;


struct D3DXQUATERNION;
D3DXQUATERNION* D3DXQuaternionMultiply( D3DXQUATERNION *pOut, const D3DXQUATERNION *pQ1, const D3DXQUATERNION *pQ2);

typedef struct D3DXQUATERNION
{
	FLOAT x, y, z, w;

	D3DXQUATERNION() {}
	D3DXQUATERNION( const FLOAT *pf) : x(pf[0]), y(pf[1]), z(pf[2]), w(pf[3]) {}
	D3DXQUATERNION( FLOAT fx, FLOAT fy, FLOAT fz, FLOAT fw) : x(fx), y(fy), z(fz), w(fw) {}

	operator FLOAT* () { return &x; }
	operator const FLOAT* () const { return &x; }

	D3DXQUATERNION& operator += ( const D3DXQUATERNION& q) { x += q.x; y += q.y; z += q.z; w += q.w; return *this; }
	D3DXQUATERNION& operator -= ( const D3DXQUATERNION& q) { x -= q.x; y -= q.y; z -= q.z; w -= q.w; return *this; }
	D3DXQUATERNION& operator *= ( const D3DXQUATERNION& q) { D3DXQuaternionMultiply( this, this, &q); return *this; }
	D3DXQUATERNION& operator *= ( FLOAT f) { x *= f; y *= f; z *= f; w *= f; return *this; }
	D3DXQUATERNION& operator /= ( FLOAT f) { FLOAT inv = 1.0f / f; x *= inv; y *= inv; z *= inv; w *= inv; return *this; }

	D3DXQUATERNION operator + () const { return *this; }
	D3DXQUATERNION operator - () const { return D3DXQUATERNION( -x, -y, -z, -w); }

	D3DXQUATERNION operator + ( const D3DXQUATERNION& q) const { return D3DXQUATERNION( x + q.x, y + q.y, z + q.z, w + q.w); }
	D3DXQUATERNION operator - ( const D3DXQUATERNION& q) const { return D3DXQUATERNION( x - q.x, y - q.y, z - q.z, w - q.w); }
	D3DXQUATERNION operator * ( const D3DXQUATERNION& q) const { D3DXQUATERNION r; D3DXQuaternionMultiply( &r, this, &q); return r; }
	D3DXQUATERNION operator * ( FLOAT f) const { return D3DXQUATERNION( x * f, y * f, z * f, w * f); }
	D3DXQUATERNION operator / ( FLOAT f) const { FLOAT inv = 1.0f / f; return D3DXQUATERNION( x * inv, y * inv, z * inv, w * inv); }

	friend D3DXQUATERNION operator * ( FLOAT f, const D3DXQUATERNION& q) { return D3DXQUATERNION( f * q.x, f * q.y, f * q.z, f * q.w); }

	BOOL operator == ( const D3DXQUATERNION& q) const { return x == q.x && y == q.y && z == q.z && w == q.w; }
	BOOL operator != ( const D3DXQUATERNION& q) const { return x != q.x || y != q.y || z != q.z || w != q.w; }
} D3DXQUATERNION, *LPD3DXQUATERNION;


typedef struct D3DXPLANE
{
	FLOAT a, b, c, d;

	D3DXPLANE() {}
	D3DXPLANE( const FLOAT *pf) : a(pf[0]), b(pf[1]), c(pf[2]), d(pf[3]) {}
	D3DXPLANE( FLOAT fa, FLOAT fb, FLOAT fc, FLOAT fd) : a(fa), b(fb), c(fc), d(fd) {}

	operator FLOAT* () { return &a; }
	operator const FLOAT* () const { return &a; }

	D3DXPLANE& operator *= ( FLOAT f) { a *= f; b *= f; c *= f; d *= f; return *this; }
	D3DXPLANE& operator /= ( FLOAT f) { FLOAT inv = 1.0f / f; a *= inv; b *= inv; c *= inv; d *= inv; return *this; }

	D3DXPLANE operator + () const { return *this; }
	D3DXPLANE operator - () const { return D3DXPLANE( -a, -b, -c, -d); }

	D3DXPLANE operator * ( FLOAT f) const { return D3DXPLANE( a * f, b * f, c * f, d * f); }
	D3DXPLANE operator / ( FLOAT f) const { FLOAT inv = 1.0f / f; return D3DXPLANE( a * inv, b * inv, c * inv, d * inv); }

	friend D3DXPLANE operator * ( FLOAT f, const D3DXPLANE& p) { return D3DXPLANE( f * p.a, f * p.b, f * p.c, f * p.d); }

	BOOL operator == ( const D3DXPLANE& p) const { return a == p.a && b == p.b && c == p.c && d == p.d; }
	BOOL operator != ( const D3DXPLANE& p) const { return a != p.a || b != p.b || c != p.c || d != p.d; }
} D3DXPLANE, *LPD3DXPLANE;


// Only declared by d3dx9mesh.h; the client never uses it.
typedef struct D3DXCOLOR
{
	FLOAT r, g, b, a;
} D3DXCOLOR, *LPD3DXCOLOR;


// 2D vector

inline FLOAT D3DXVec2Length( const D3DXVECTOR2 *pV)
{
	return sqrtf( pV->x * pV->x + pV->y * pV->y);
}

inline FLOAT D3DXVec2LengthSq( const D3DXVECTOR2 *pV)
{
	return pV->x * pV->x + pV->y * pV->y;
}

inline FLOAT D3DXVec2Dot( const D3DXVECTOR2 *pV1, const D3DXVECTOR2 *pV2)
{
	return pV1->x * pV2->x + pV1->y * pV2->y;
}

inline D3DXVECTOR2* D3DXVec2Normalize( D3DXVECTOR2 *pOut, const D3DXVECTOR2 *pV)
{
	FLOAT fLength = D3DXVec2Length(pV);

	if( fLength > 0.0f )
	{
		pOut->x = pV->x / fLength;
		pOut->y = pV->y / fLength;
	}
	else
		pOut->x = pOut->y = 0.0f;

	return pOut;
}


// 3D vector

inline FLOAT D3DXVec3Length( const D3DXVECTOR3 *pV)
{
	return sqrtf( pV->x * pV->x + pV->y * pV->y + pV->z * pV->z);
}

inline FLOAT D3DXVec3LengthSq( const D3DXVECTOR3 *pV)
{
	return pV->x * pV->x + pV->y * pV->y + pV->z * pV->z;
}

inline FLOAT D3DXVec3Dot( const D3DXVECTOR3 *pV1, const D3DXVECTOR3 *pV2)
{
	return pV1->x * pV2->x + pV1->y * pV2->y + pV1->z * pV2->z;
}

inline D3DXVECTOR3* D3DXVec3Cross( D3DXVECTOR3 *pOut, const D3DXVECTOR3 *pV1, const D3DXVECTOR3 *pV2)
{
	D3DXVECTOR3 v(
		pV1->y * pV2->z - pV1->z * pV2->y,
		pV1->z * pV2->x - pV1->x * pV2->z,
		pV1->x * pV2->y - pV1->y * pV2->x);

	*pOut = v;
	return pOut;
}

inline D3DXVECTOR3* D3DXVec3Lerp( D3DXVECTOR3 *pOut, const D3DXVECTOR3 *pV1, const D3DXVECTOR3 *pV2, FLOAT s)
{
	pOut->x = pV1->x + s * (pV2->x - pV1->x);
	pOut->y = pV1->y + s * (pV2->y - pV1->y);
	pOut->z = pV1->z + s * (pV2->z - pV1->z);

	return pOut;
}

inline D3DXVECTOR3* D3DXVec3Normalize( D3DXVECTOR3 *pOut, const D3DXVECTOR3 *pV)
{
	FLOAT fLength = D3DXVec3Length(pV);

	if( fLength > 0.0f )
	{
		pOut->x = pV->x / fLength;
		pOut->y = pV->y / fLength;
		pOut->z = pV->z / fLength;
	}
	else
		pOut->x = pOut->y = pOut->z = 0.0f;

	return pOut;
}

// (x, y, z, 1) * M
inline D3DXVECTOR4* D3DXVec3Transform( D3DXVECTOR4 *pOut, const D3DXVECTOR3 *pV, const D3DXMATRIX *pM)
{
	D3DXVECTOR4 v(
		pV->x * pM->_11 + pV->y * pM->_21 + pV->z * pM->_31 + pM->_41,
		pV->x * pM->_12 + pV->y * pM->_22 + pV->z * pM->_32 + pM->_42,
		pV->x * pM->_13 + pV->y * pM->_23 + pV->z * pM->_33 + pM->_43,
		pV->x * pM->_14 + pV->y * pM->_24 + pV->z * pM->_34 + pM->_44);

	*pOut = v;
	return pOut;
}

// (x, y, z, 1) * M, divided by w
inline D3DXVECTOR3* D3DXVec3TransformCoord( D3DXVECTOR3 *pOut, const D3DXVECTOR3 *pV, const D3DXMATRIX *pM)
{
	D3DXVECTOR4 v;
	D3DXVec3Transform( &v, pV, pM);

	pOut->x = v.x / v.w;
	pOut->y = v.y / v.w;
	pOut->z = v.z / v.w;

	return pOut;
}


// Matrix

inline D3DXMATRIX* D3DXMatrixIdentity( D3DXMATRIX *pOut)
{
	memset( pOut, 0, sizeof(D3DXMATRIX));
	pOut->_11 = pOut->_22 = pOut->_33 = pOut->_44 = 1.0f;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixMultiply( D3DXMATRIX *pOut, const D3DXMATRIX *pM1, const D3DXMATRIX *pM2)
{
	D3DXMATRIX r;

	for( int i=0; i<4; i++)
		for( int j=0; j<4; j++)
		{
			r.m[i][j] =
				pM1->m[i][0] * pM2->m[0][j] +
				pM1->m[i][1] * pM2->m[1][j] +
				pM1->m[i][2] * pM2->m[2][j] +
				pM1->m[i][3] * pM2->m[3][j];
		}

	*pOut = r;
	return pOut;
}

inline D3DXMATRIX* D3DXMatrixTranspose( D3DXMATRIX *pOut, const D3DXMATRIX *pM)
{
	D3DXMATRIX r;

	for( int i=0; i<4; i++)
		for( int j=0; j<4; j++)
			r.m[i][j] = pM->m[j][i];

	*pOut = r;
	return pOut;
}

// Returns NULL and leaves pOut untouched when the matrix is singular.
inline D3DXMATRIX* D3DXMatrixInverse( D3DXMATRIX *pOut, FLOAT *pDeterminant, const D3DXMATRIX *pM)
{
	const FLOAT *a = &pM->_11;
	FLOAT inv[16];

	inv[0] = a[5] * a[10] * a[15] - a[5] * a[11] * a[14] - a[9] * a[6] * a[15] + a[9] * a[7] * a[14] + a[13] * a[6] * a[11] - a[13] * a[7] * a[10];
	inv[4] = -a[4] * a[10] * a[15] + a[4] * a[11] * a[14] + a[8] * a[6] * a[15] - a[8] * a[7] * a[14] - a[12] * a[6] * a[11] + a[12] * a[7] * a[10];
	inv[8] = a[4] * a[9] * a[15] - a[4] * a[11] * a[13] - a[8] * a[5] * a[15] + a[8] * a[7] * a[13] + a[12] * a[5] * a[11] - a[12] * a[7] * a[9];
	inv[12] = -a[4] * a[9] * a[14] + a[4] * a[10] * a[13] + a[8] * a[5] * a[14] - a[8] * a[6] * a[13] - a[12] * a[5] * a[10] + a[12] * a[6] * a[9];

	FLOAT fDet = a[0] * inv[0] + a[1] * inv[4] + a[2] * inv[8] + a[3] * inv[12];

	if(pDeterminant)
		*pDeterminant = fDet;

	if( fDet == 0.0f )
		return NULL;

	inv[1] = -a[1] * a[10] * a[15] + a[1] * a[11] * a[14] + a[9] * a[2] * a[15] - a[9] * a[3] * a[14] - a[13] * a[2] * a[11] + a[13] * a[3] * a[10];
	inv[5] = a[0] * a[10] * a[15] - a[0] * a[11] * a[14] - a[8] * a[2] * a[15] + a[8] * a[3] * a[14] + a[12] * a[2] * a[11] - a[12] * a[3] * a[10];
	inv[9] = -a[0] * a[9] * a[15] + a[0] * a[11] * a[13] + a[8] * a[1] * a[15] - a[8] * a[3] * a[13] - a[12] * a[1] * a[11] + a[12] * a[3] * a[9];
	inv[13] = a[0] * a[9] * a[14] - a[0] * a[10] * a[13] - a[8] * a[1] * a[14] + a[8] * a[2] * a[13] + a[12] * a[1] * a[10] - a[12] * a[2] * a[9];
	inv[2] = a[1] * a[6] * a[15] - a[1] * a[7] * a[14] - a[5] * a[2] * a[15] + a[5] * a[3] * a[14] + a[13] * a[2] * a[7] - a[13] * a[3] * a[6];
	inv[6] = -a[0] * a[6] * a[15] + a[0] * a[7] * a[14] + a[4] * a[2] * a[15] - a[4] * a[3] * a[14] - a[12] * a[2] * a[7] + a[12] * a[3] * a[6];
	inv[10] = a[0] * a[5] * a[15] - a[0] * a[7] * a[13] - a[4] * a[1] * a[15] + a[4] * a[3] * a[13] + a[12] * a[1] * a[7] - a[12] * a[3] * a[5];
	inv[14] = -a[0] * a[5] * a[14] + a[0] * a[6] * a[13] + a[4] * a[1] * a[14] - a[4] * a[2] * a[13] - a[12] * a[1] * a[6] + a[12] * a[2] * a[5];
	inv[3] = -a[1] * a[6] * a[11] + a[1] * a[7] * a[10] + a[5] * a[2] * a[11] - a[5] * a[3] * a[10] - a[9] * a[2] * a[7] + a[9] * a[3] * a[6];
	inv[7] = a[0] * a[6] * a[11] - a[0] * a[7] * a[10] - a[4] * a[2] * a[11] + a[4] * a[3] * a[10] + a[8] * a[2] * a[7] - a[8] * a[3] * a[6];
	inv[11] = -a[0] * a[5] * a[11] + a[0] * a[7] * a[9] + a[4] * a[1] * a[11] - a[4] * a[3] * a[9] - a[8] * a[1] * a[7] + a[8] * a[3] * a[5];
	inv[15] = a[0] * a[5] * a[10] - a[0] * a[6] * a[9] - a[4] * a[1] * a[10] + a[4] * a[2] * a[9] + a[8] * a[1] * a[6] - a[8] * a[2] * a[5];

	FLOAT fInvDet = 1.0f / fDet;
	for( int i=0; i<16; i++)
		(&pOut->_11)[i] = inv[i] * fInvDet;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixScaling( D3DXMATRIX *pOut, FLOAT sx, FLOAT sy, FLOAT sz)
{
	D3DXMatrixIdentity(pOut);
	pOut->_11 = sx;
	pOut->_22 = sy;
	pOut->_33 = sz;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixTranslation( D3DXMATRIX *pOut, FLOAT x, FLOAT y, FLOAT z)
{
	D3DXMatrixIdentity(pOut);
	pOut->_41 = x;
	pOut->_42 = y;
	pOut->_43 = z;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixRotationX( D3DXMATRIX *pOut, FLOAT fAngle)
{
	FLOAT s = sinf(fAngle);
	FLOAT c = cosf(fAngle);

	D3DXMatrixIdentity(pOut);
	pOut->_22 = c;
	pOut->_23 = s;
	pOut->_32 = -s;
	pOut->_33 = c;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixRotationY( D3DXMATRIX *pOut, FLOAT fAngle)
{
	FLOAT s = sinf(fAngle);
	FLOAT c = cosf(fAngle);

	D3DXMatrixIdentity(pOut);
	pOut->_11 = c;
	pOut->_13 = -s;
	pOut->_31 = s;
	pOut->_33 = c;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixRotationZ( D3DXMATRIX *pOut, FLOAT fAngle)
{
	FLOAT s = sinf(fAngle);
	FLOAT c = cosf(fAngle);

	D3DXMatrixIdentity(pOut);
	pOut->_11 = c;
	pOut->_12 = s;
	pOut->_21 = -s;
	pOut->_22 = c;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixRotationAxis( D3DXMATRIX *pOut, const D3DXVECTOR3 *pV, FLOAT fAngle)
{
	D3DXVECTOR3 v;
	D3DXVec3Normalize( &v, pV);

	FLOAT s = sinf(fAngle);
	FLOAT c = cosf(fAngle);
	FLOAT t = 1.0f - c;

	D3DXMatrixIdentity(pOut);
	pOut->_11 = t * v.x * v.x + c;
	pOut->_12 = t * v.x * v.y + s * v.z;
	pOut->_13 = t * v.x * v.z - s * v.y;
	pOut->_21 = t * v.x * v.y - s * v.z;
	pOut->_22 = t * v.y * v.y + c;
	pOut->_23 = t * v.y * v.z + s * v.x;
	pOut->_31 = t * v.x * v.z + s * v.y;
	pOut->_32 = t * v.y * v.z - s * v.x;
	pOut->_33 = t * v.z * v.z + c;

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixRotationQuaternion( D3DXMATRIX *pOut, const D3DXQUATERNION *pQ)
{
	FLOAT x = pQ->x, y = pQ->y, z = pQ->z, w = pQ->w;

	D3DXMatrixIdentity(pOut);
	pOut->_11 = 1.0f - 2.0f * (y * y + z * z);
	pOut->_12 = 2.0f * (x * y + z * w);
	pOut->_13 = 2.0f * (x * z - y * w);
	pOut->_21 = 2.0f * (x * y - z * w);
	pOut->_22 = 1.0f - 2.0f * (x * x + z * z);
	pOut->_23 = 2.0f * (y * z + x * w);
	pOut->_31 = 2.0f * (x * z + y * w);
	pOut->_32 = 2.0f * (y * z - x * w);
	pOut->_33 = 1.0f - 2.0f * (x * x + y * y);

	return pOut;
}

// Roll around Z, then pitch around X, then yaw around Y.
inline D3DXMATRIX* D3DXMatrixRotationYawPitchRoll( D3DXMATRIX *pOut, FLOAT fYaw, FLOAT fPitch, FLOAT fRoll)
{
	D3DXMATRIX vRoll, vPitch, vYaw;

	D3DXMatrixRotationZ( &vRoll, fRoll);
	D3DXMatrixRotationX( &vPitch, fPitch);
	D3DXMatrixRotationY( &vYaw, fYaw);

	D3DXMatrixMultiply( pOut, &vRoll, &vPitch);
	return D3DXMatrixMultiply( pOut, pOut, &vYaw);
}

inline D3DXMATRIX* D3DXMatrixPerspectiveFovLH( D3DXMATRIX *pOut, FLOAT fFovY, FLOAT fAspect, FLOAT fNear, FLOAT fFar)
{
	FLOAT fScaleY = 1.0f / tanf(fFovY / 2.0f);

	memset( pOut, 0, sizeof(D3DXMATRIX));
	pOut->_11 = fScaleY / fAspect;
	pOut->_22 = fScaleY;
	pOut->_33 = fFar / (fFar - fNear);
	pOut->_34 = 1.0f;
	pOut->_43 = -fNear * fFar / (fFar - fNear);

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixOrthoLH( D3DXMATRIX *pOut, FLOAT fWidth, FLOAT fHeight, FLOAT fNear, FLOAT fFar)
{
	D3DXMatrixIdentity(pOut);
	pOut->_11 = 2.0f / fWidth;
	pOut->_22 = 2.0f / fHeight;
	pOut->_33 = 1.0f / (fFar - fNear);
	pOut->_43 = fNear / (fNear - fFar);

	return pOut;
}

inline D3DXMATRIX* D3DXMatrixLookAtLH( D3DXMATRIX *pOut, const D3DXVECTOR3 *pEye, const D3DXVECTOR3 *pAt, const D3DXVECTOR3 *pUp)
{
	D3DXVECTOR3 vZ = *pAt - *pEye;
	D3DXVECTOR3 vX;
	D3DXVECTOR3 vY;

	D3DXVec3Normalize( &vZ, &vZ);
	D3DXVec3Cross( &vX, pUp, &vZ);
	D3DXVec3Normalize( &vX, &vX);
	D3DXVec3Cross( &vY, &vZ, &vX);

	pOut->_11 = vX.x; pOut->_12 = vY.x; pOut->_13 = vZ.x; pOut->_14 = 0.0f;
	pOut->_21 = vX.y; pOut->_22 = vY.y; pOut->_23 = vZ.y; pOut->_24 = 0.0f;
	pOut->_31 = vX.z; pOut->_32 = vY.z; pOut->_33 = vZ.z; pOut->_34 = 0.0f;
	pOut->_41 = -D3DXVec3Dot( &vX, pEye);
	pOut->_42 = -D3DXVec3Dot( &vY, pEye);
	pOut->_43 = -D3DXVec3Dot( &vZ, pEye);
	pOut->_44 = 1.0f;

	return pOut;
}


// Quaternion

inline FLOAT D3DXQuaternionLengthSq( const D3DXQUATERNION *pQ)
{
	return pQ->x * pQ->x + pQ->y * pQ->y + pQ->z * pQ->z + pQ->w * pQ->w;
}

inline FLOAT D3DXQuaternionDot( const D3DXQUATERNION *pQ1, const D3DXQUATERNION *pQ2)
{
	return pQ1->x * pQ2->x + pQ1->y * pQ2->y + pQ1->z * pQ2->z + pQ1->w * pQ2->w;
}

inline D3DXQUATERNION* D3DXQuaternionIdentity( D3DXQUATERNION *pOut)
{
	pOut->x = pOut->y = pOut->z = 0.0f;
	pOut->w = 1.0f;

	return pOut;
}

// The rotation of pQ1 followed by the rotation of pQ2 (pQ2 * pQ1).
inline D3DXQUATERNION* D3DXQuaternionMultiply( D3DXQUATERNION *pOut, const D3DXQUATERNION *pQ1, const D3DXQUATERNION *pQ2)
{
	D3DXQUATERNION q(
		pQ2->w * pQ1->x + pQ2->x * pQ1->w + pQ2->y * pQ1->z - pQ2->z * pQ1->y,
		pQ2->w * pQ1->y - pQ2->x * pQ1->z + pQ2->y * pQ1->w + pQ2->z * pQ1->x,
		pQ2->w * pQ1->z + pQ2->x * pQ1->y - pQ2->y * pQ1->x + pQ2->z * pQ1->w,
		pQ2->w * pQ1->w - pQ2->x * pQ1->x - pQ2->y * pQ1->y - pQ2->z * pQ1->z);

	*pOut = q;
	return pOut;
}

inline D3DXQUATERNION* D3DXQuaternionNormalize( D3DXQUATERNION *pOut, const D3DXQUATERNION *pQ)
{
	FLOAT fLength = sqrtf(D3DXQuaternionLengthSq(pQ));

	if( fLength > 0.0f )
	{
		pOut->x = pQ->x / fLength;
		pOut->y = pQ->y / fLength;
		pOut->z = pQ->z / fLength;
		pOut->w = pQ->w / fLength;
	}
	else
		pOut->x = pOut->y = pOut->z = pOut->w = 0.0f;

	return pOut;
}

inline D3DXQUATERNION* D3DXQuaternionInverse( D3DXQUATERNION *pOut, const D3DXQUATERNION *pQ)
{
	FLOAT fNorm = D3DXQuaternionLengthSq(pQ);

	pOut->x = -pQ->x / fNorm;
	pOut->y = -pQ->y / fNorm;
	pOut->z = -pQ->z / fNorm;
	pOut->w = pQ->w / fNorm;

	return pOut;
}

inline D3DXQUATERNION* D3DXQuaternionRotationAxis( D3DXQUATERNION *pOut, const D3DXVECTOR3 *pV, FLOAT fAngle)
{
	D3DXVECTOR3 v;
	D3DXVec3Normalize( &v, pV);

	FLOAT s = sinf(fAngle / 2.0f);

	pOut->x = v.x * s;
	pOut->y = v.y * s;
	pOut->z = v.z * s;
	pOut->w = cosf(fAngle / 2.0f);

	return pOut;
}

// The matrix must be a pure rotation.
inline D3DXQUATERNION* D3DXQuaternionRotationMatrix( D3DXQUATERNION *pOut, const D3DXMATRIX *pM)
{
	FLOAT fTrace = pM->_11 + pM->_22 + pM->_33 + 1.0f;

	if( fTrace > 1.0f )
	{
		FLOAT s = 2.0f * sqrtf(fTrace);

		pOut->x = (pM->_23 - pM->_32) / s;
		pOut->y = (pM->_31 - pM->_13) / s;
		pOut->z = (pM->_12 - pM->_21) / s;
		pOut->w = 0.25f * s;
	}
	else if( pM->_11 >= pM->_22 && pM->_11 >= pM->_33 )
	{
		FLOAT s = 2.0f * sqrtf(1.0f + pM->_11 - pM->_22 - pM->_33);

		pOut->x = 0.25f * s;
		pOut->y = (pM->_12 + pM->_21) / s;
		pOut->z = (pM->_13 + pM->_31) / s;
		pOut->w = (pM->_23 - pM->_32) / s;
	}
	else if( pM->_22 >= pM->_33 )
	{
		FLOAT s = 2.0f * sqrtf(1.0f + pM->_22 - pM->_11 - pM->_33);

		pOut->x = (pM->_12 + pM->_21) / s;
		pOut->y = 0.25f * s;
		pOut->z = (pM->_23 + pM->_32) / s;
		pOut->w = (pM->_31 - pM->_13) / s;
	}
	else
	{
		FLOAT s = 2.0f * sqrtf(1.0f + pM->_33 - pM->_11 - pM->_22);

		pOut->x = (pM->_13 + pM->_31) / s;
		pOut->y = (pM->_23 + pM->_32) / s;
		pOut->z = 0.25f * s;
		pOut->w = (pM->_12 - pM->_21) / s;
	}

	return pOut;
}

inline D3DXQUATERNION* D3DXQuaternionSlerp( D3DXQUATERNION *pOut, const D3DXQUATERNION *pQ1, const D3DXQUATERNION *pQ2, FLOAT t)
{
	FLOAT fDot = D3DXQuaternionDot( pQ1, pQ2);
	FLOAT fSign = 1.0f;

	if( fDot < 0.0f )
	{
		fDot = -fDot;
		fSign = -1.0f;
	}

	FLOAT f1 = 1.0f - t;
	FLOAT f2 = t;

	if( 1.0f - fDot > 0.001f )
	{
		FLOAT fTheta = acosf(fDot);
		FLOAT fSin = sinf(fTheta);

		f1 = sinf(fTheta * f1) / fSin;
		f2 = sinf(fTheta * f2) / fSin;
	}
	f2 *= fSign;

	pOut->x = f1 * pQ1->x + f2 * pQ2->x;
	pOut->y = f1 * pQ1->y + f2 * pQ2->y;
	pOut->z = f1 * pQ1->z + f2 * pQ2->z;
	pOut->w = f1 * pQ1->w + f2 * pQ2->w;

	return pOut;
}

// ln of a unit quaternion (cos a, v sin a) is (0, v a).
inline D3DXQUATERNION* D3DXQuaternionLn( D3DXQUATERNION *pOut, const D3DXQUATERNION *pQ)
{
	FLOAT t = 1.0f;

	if( pQ->w < 1.0f && pQ->w > -1.0f )
		t = acosf(pQ->w) / sqrtf(1.0f - pQ->w * pQ->w);

	pOut->x = t * pQ->x;
	pOut->y = t * pQ->y;
	pOut->z = t * pQ->z;
	pOut->w = 0.0f;

	return pOut;
}


// Matrix built from parts

inline HRESULT D3DXMatrixDecompose( D3DXVECTOR3 *pOutScale, D3DXQUATERNION *pOutRotation, D3DXVECTOR3 *pOutTranslation, const D3DXMATRIX *pM)
{
	pOutScale->x = sqrtf( pM->_11 * pM->_11 + pM->_12 * pM->_12 + pM->_13 * pM->_13);
	pOutScale->y = sqrtf( pM->_21 * pM->_21 + pM->_22 * pM->_22 + pM->_23 * pM->_23);
	pOutScale->z = sqrtf( pM->_31 * pM->_31 + pM->_32 * pM->_32 + pM->_33 * pM->_33);

	pOutTranslation->x = pM->_41;
	pOutTranslation->y = pM->_42;
	pOutTranslation->z = pM->_43;

	if( pOutScale->x == 0.0f || pOutScale->y == 0.0f || pOutScale->z == 0.0f )
		return D3DERR_INVALIDCALL;

	D3DXMATRIX vROT;
	D3DXMatrixIdentity(&vROT);

	FLOAT fInvX = 1.0f / pOutScale->x;
	FLOAT fInvY = 1.0f / pOutScale->y;
	FLOAT fInvZ = 1.0f / pOutScale->z;

	for( int i=0; i<3; i++)
	{
		vROT.m[0][i] = pM->m[0][i] * fInvX;
		vROT.m[1][i] = pM->m[1][i] * fInvY;
		vROT.m[2][i] = pM->m[2][i] * fInvZ;
	}

	D3DXQuaternionRotationMatrix( pOutRotation, &vROT);
	return S_OK;
}

// Msc^-1 * Msr^-1 * Ms * Msr * Msc * Mrc^-1 * Mr * Mrc * Mt; a NULL part is skipped.
inline D3DXMATRIX* D3DXMatrixTransformation(
	D3DXMATRIX *pOut,
	const D3DXVECTOR3 *pScalingCenter,
	const D3DXQUATERNION *pScalingRotation,
	const D3DXVECTOR3 *pScaling,
	const D3DXVECTOR3 *pRotationCenter,
	const D3DXQUATERNION *pRotation,
	const D3DXVECTOR3 *pTranslation)
{
	D3DXMATRIX r;
	D3DXMATRIX t;

	D3DXMatrixIdentity(&r);
	if(pScaling)
	{
		D3DXQUATERNION qSR;
		D3DXQuaternionIdentity(&qSR);

		if(pScalingRotation)
			qSR = *pScalingRotation;

		D3DXVECTOR3 vSC( 0.0f, 0.0f, 0.0f);
		if(pScalingCenter)
			vSC = *pScalingCenter;

		D3DXMATRIX vSR;
		D3DXMATRIX vSRI;
		D3DXQUATERNION qSRI;

		D3DXMatrixRotationQuaternion( &vSR, &qSR);
		D3DXQuaternionInverse( &qSRI, &qSR);
		D3DXMatrixRotationQuaternion( &vSRI, &qSRI);

		D3DXMatrixTranslation( &r, -vSC.x, -vSC.y, -vSC.z);
		D3DXMatrixMultiply( &r, &r, &vSRI);
		D3DXMatrixScaling( &t, pScaling->x, pScaling->y, pScaling->z);
		D3DXMatrixMultiply( &r, &r, &t);
		D3DXMatrixMultiply( &r, &r, &vSR);
		D3DXMatrixTranslation( &t, vSC.x, vSC.y, vSC.z);
		D3DXMatrixMultiply( &r, &r, &t);
	}

	if(pRotation)
	{
		D3DXVECTOR3 vRC( 0.0f, 0.0f, 0.0f);
		if(pRotationCenter)
			vRC = *pRotationCenter;

		D3DXMatrixTranslation( &t, -vRC.x, -vRC.y, -vRC.z);
		D3DXMatrixMultiply( &r, &r, &t);
		D3DXMatrixRotationQuaternion( &t, pRotation);
		D3DXMatrixMultiply( &r, &r, &t);
		D3DXMatrixTranslation( &t, vRC.x, vRC.y, vRC.z);
		D3DXMatrixMultiply( &r, &r, &t);
	}

	if(pTranslation)
	{
		D3DXMatrixTranslation( &t, pTranslation->x, pTranslation->y, pTranslation->z);
		D3DXMatrixMultiply( &r, &r, &t);
	}

	*pOut = r;
	return pOut;
}

inline D3DXMATRIX* D3DXMatrixTransformation2D(
	D3DXMATRIX *pOut,
	const D3DXVECTOR2 *pScalingCenter,
	FLOAT fScalingRotation,
	const D3DXVECTOR2 *pScaling,
	const D3DXVECTOR2 *pRotationCenter,
	FLOAT fRotation,
	const D3DXVECTOR2 *pTranslation)
{
	D3DXVECTOR3 vSC( 0.0f, 0.0f, 0.0f);
	D3DXVECTOR3 vS( 1.0f, 1.0f, 1.0f);
	D3DXVECTOR3 vRC( 0.0f, 0.0f, 0.0f);
	D3DXVECTOR3 vT( 0.0f, 0.0f, 0.0f);

	if(pScalingCenter)
		vSC = D3DXVECTOR3( pScalingCenter->x, pScalingCenter->y, 0.0f);

	if(pScaling)
		vS = D3DXVECTOR3( pScaling->x, pScaling->y, 1.0f);

	if(pRotationCenter)
		vRC = D3DXVECTOR3( pRotationCenter->x, pRotationCenter->y, 0.0f);

	if(pTranslation)
		vT = D3DXVECTOR3( pTranslation->x, pTranslation->y, 0.0f);

	D3DXQUATERNION qSR( 0.0f, 0.0f, sinf(fScalingRotation / 2.0f), cosf(fScalingRotation / 2.0f));
	D3DXQUATERNION qR( 0.0f, 0.0f, sinf(fRotation / 2.0f), cosf(fRotation / 2.0f));

	return D3DXMatrixTransformation( pOut, &vSC, &qSR, &vS, &vRC, &qR, &vT);
}


// Plane

inline FLOAT D3DXPlaneDotCoord( const D3DXPLANE *pP, const D3DXVECTOR3 *pV)
{
	return pP->a * pV->x + pP->b * pV->y + pP->c * pV->z + pP->d;
}

inline FLOAT D3DXPlaneDotNormal( const D3DXPLANE *pP, const D3DXVECTOR3 *pV)
{
	return pP->a * pV->x + pP->b * pV->y + pP->c * pV->z;
}

inline D3DXPLANE* D3DXPlaneFromPointNormal( D3DXPLANE *pOut, const D3DXVECTOR3 *pPoint, const D3DXVECTOR3 *pNormal)
{
	pOut->a = pNormal->x;
	pOut->b = pNormal->y;
	pOut->c = pNormal->z;
	pOut->d = -D3DXVec3Dot( pPoint, pNormal);

	return pOut;
}

inline D3DXPLANE* D3DXPlaneFromPoints( D3DXPLANE *pOut, const D3DXVECTOR3 *pV1, const D3DXVECTOR3 *pV2, const D3DXVECTOR3 *pV3)
{
	D3DXVECTOR3 vEdge1 = *pV2 - *pV1;
	D3DXVECTOR3 vEdge2 = *pV3 - *pV1;
	D3DXVECTOR3 vNormal;

	D3DXVec3Cross( &vNormal, &vEdge1, &vEdge2);
	D3DXVec3Normalize( &vNormal, &vNormal);

	return D3DXPlaneFromPointNormal( pOut, pV1, &vNormal);
}

inline D3DXPLANE* D3DXPlaneNormalize( D3DXPLANE *pOut, const D3DXPLANE *pP)
{
	FLOAT fLength = sqrtf( pP->a * pP->a + pP->b * pP->b + pP->c * pP->c);

	if( fLength > 0.0f )
	{
		pOut->a = pP->a / fLength;
		pOut->b = pP->b / fLength;
		pOut->c = pP->c / fLength;
		pOut->d = pP->d / fLength;
	}
	else
		pOut->a = pOut->b = pOut->c = pOut->d = 0.0f;

	return pOut;
}

// The point where the line through pV1 and pV2 crosses the plane, NULL if parallel.
inline D3DXVECTOR3* D3DXPlaneIntersectLine( D3DXVECTOR3 *pOut, const D3DXPLANE *pP, const D3DXVECTOR3 *pV1, const D3DXVECTOR3 *pV2)
{
	D3DXVECTOR3 vDIR = *pV2 - *pV1;
	FLOAT fDot = D3DXPlaneDotNormal( pP, &vDIR);

	if( fDot == 0.0f )
		return NULL;

	FLOAT t = -D3DXPlaneDotCoord( pP, pV1) / fDot;

	pOut->x = pV1->x + t * vDIR.x;
	pOut->y = pV1->y + t * vDIR.y;
	pOut->z = pV1->z + t * vDIR.z;

	return pOut;
}

// The plane as a row vector times pM (pass the inverse transpose of the transform).
inline D3DXPLANE* D3DXPlaneTransform( D3DXPLANE *pOut, const D3DXPLANE *pP, const D3DXMATRIX *pM)
{
	D3DXPLANE p(
		pP->a * pM->_11 + pP->b * pM->_21 + pP->c * pM->_31 + pP->d * pM->_41,
		pP->a * pM->_12 + pP->b * pM->_22 + pP->c * pM->_32 + pP->d * pM->_42,
		pP->a * pM->_13 + pP->b * pM->_23 + pP->c * pM->_33 + pP->d * pM->_43,
		pP->a * pM->_14 + pP->b * pM->_24 + pP->c * pM->_34 + pP->d * pM->_44);

	*pOut = p;
	return pOut;
}


// Picking (also declared by d3dx9mesh.h, hence the C linkage)

// Ray against triangle: on a hit, pU/pV are the barycentric coordinates of
// pV1/pV2 and pDist is the distance along pRayDir, in units of its length.
extern "C" inline BOOL WINAPI D3DXIntersectTri(
	const D3DXVECTOR3 *pV0,
	const D3DXVECTOR3 *pV1,
	const D3DXVECTOR3 *pV2,
	const D3DXVECTOR3 *pRayPos,
	const D3DXVECTOR3 *pRayDir,
	FLOAT *pU,
	FLOAT *pV,
	FLOAT *pDist)
{
	D3DXVECTOR3 vEdge1 = *pV1 - *pV0;
	D3DXVECTOR3 vEdge2 = *pV2 - *pV0;
	D3DXVECTOR3 vP;

	D3DXVec3Cross( &vP, pRayDir, &vEdge2);
	FLOAT fDet = D3DXVec3Dot( &vEdge1, &vP);

	if( fDet == 0.0f )
		return FALSE;

	FLOAT fInvDet = 1.0f / fDet;
	D3DXVECTOR3 vT = *pRayPos - *pV0;

	FLOAT u = D3DXVec3Dot( &vT, &vP) * fInvDet;
	if( u < 0.0f || u > 1.0f )
		return FALSE;

	D3DXVECTOR3 vQ;
	D3DXVec3Cross( &vQ, &vT, &vEdge1);

	FLOAT v = D3DXVec3Dot( pRayDir, &vQ) * fInvDet;
	if( v < 0.0f || u + v > 1.0f )
		return FALSE;

	FLOAT fDist = D3DXVec3Dot( &vEdge2, &vQ) * fInvDet;
	if( fDist < 0.0f )
		return FALSE;

	if(pU)
		*pU = u;

	if(pV)
		*pV = v;

	if(pDist)
		*pDist = fDist;

	return TRUE;
}

// TRUE when the ray (not the line behind it) goes through the sphere.
extern "C" inline BOOL WINAPI D3DXSphereBoundProbe( const D3DXVECTOR3 *pCenter, FLOAT fRadius, const D3DXVECTOR3 *pRayPos, const D3DXVECTOR3 *pRayDir)
{
	D3DXVECTOR3 vDiff = *pRayPos - *pCenter;

	FLOAT a = D3DXVec3LengthSq(pRayDir);
	FLOAT b = D3DXVec3Dot( &vDiff, pRayDir);
	FLOAT c = D3DXVec3LengthSq(&vDiff) - fRadius * fRadius;
	FLOAT d = b * b - a * c;

	return d > 0.0f && sqrtf(d) > b ? TRUE : FALSE;
}
