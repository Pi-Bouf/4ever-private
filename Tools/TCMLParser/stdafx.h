// stdafx.h : include file for standard system include files,
// or project specific include files that are used frequently,
// but are changed infrequently

struct DXGI_JPEG_DC_HUFFMAN_TABLE
{
	typedef int CodeCounts[12];
	typedef int CodeValues[12];
};

struct DXGI_JPEG_AC_HUFFMAN_TABLE
{
	typedef int CodeCounts[16];
	typedef int CodeValues[162];
};

struct DXGI_JPEG_QUANTIZATION_TABLE
{
	typedef int  Elements[64];
};

#pragma once

#ifndef VC_EXTRALEAN
#define VC_EXTRALEAN		// Exclude rarely-used stuff from Windows headers
#endif


#define _ATL_CSTRING_EXPLICIT_CONSTRUCTORS	// some CString constructors will be explicit

// turns off MFC's hiding of some common and often safely ignored warning messages
#define _AFX_ALL_WARNINGS

#include <afxwin.h>         // MFC core and standard components
#include <afxext.h>         // MFC extensions
#include <afxdisp.h>        // MFC Automation classes
#include <afxsock.h>

#include <afxdtctl.h>		// MFC support for Internet Explorer 4 Common Controls
#ifndef _AFX_NO_AFXCMN_SUPPORT
#include <afxcmn.h>			// MFC support for Windows Common Controls
#include <T3D.h>
#endif // _AFX_NO_AFXCMN_SUPPORT

