// TachyonApp.h: interface for the CTachyonApp class.
//
//////////////////////////////////////////////////////////////////////

#if !defined __TACHYONAPP_H
#define __TACHYONAPP_H


#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#ifndef __AFXWIN_H__
	#error include 'stdafx.h' before including this file for PCH
#endif

class CTachyonApp : public CWinApp
{
public:
	CTachyonApp();
	virtual ~CTachyonApp();

// Overrides
	// ClassWizard generated virtual function overrides
	//{{AFX_VIRTUAL(CTachyonApp)
	public:
	virtual int Run();
	//}}AFX_VIRTUAL
	virtual void LoadStdProfileSettings();
	virtual void SaveStdProfileSettings();
	virtual BYTE MainProc();

	// Settings are stored in a config.ini file (see CGameConfig), not the Windows registry.
	// These shadow the CWinApp profile methods so every existing GetProfile*/WriteProfile*
	// call site is transparently redirected to the file-based loader.
	UINT    GetProfileInt(LPCTSTR lpszSection, LPCTSTR lpszEntry, int nDefault);
	CString GetProfileString(LPCTSTR lpszSection, LPCTSTR lpszEntry, LPCTSTR lpszDefault = NULL);
	BOOL    WriteProfileInt(LPCTSTR lpszSection, LPCTSTR lpszEntry, int nValue);
	BOOL    WriteProfileString(LPCTSTR lpszSection, LPCTSTR lpszEntry, LPCTSTR lpszValue);

protected:
	CTachyonWnd *m_pTachyonWnd;

	HACCEL m_hAccel;
	DWORD m_dwSLEEP;

public:
// Implementation
	//{{AFX_MSG(CTachyonApp)
	//}}AFX_MSG
	DECLARE_MESSAGE_MAP()
};

#endif // !defined __TACHYONAPP_H
