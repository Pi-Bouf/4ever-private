// GameConfig.h : interface for the CGameConfig class.
//
// File-based configuration loader shared by TClient and TLauncher. Replaces the old
// Windows-registry storage (HKCU\Software\4Story and HKCU\Software\TEST\4story) with a
// single plain-text "config.ini" located next to the running executable.
//
// The public API deliberately mirrors the MFC CWinApp profile methods (section / key /
// default) so existing call sites map onto it one-to-one. The no-default overloads pull
// the value from the central defaults table below (the single source of truth), so a
// freshly-installed game with no config.ini still starts with sane values.
//
//////////////////////////////////////////////////////////////////////

#if !defined __GAMECONFIG_H
#define __GAMECONFIG_H

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#include <afx.h>      // CString
#include <windows.h>  // LPCTSTR / MAX_PATH

class CGameConfig
{
public:
	// Process-wide singleton.
	static CGameConfig& Instance();

	// Resolve "<exe directory>\config.ini". Safe to call more than once; also happens
	// lazily on first access, so calling it explicitly from InitInstance is optional.
	void    Init();
	LPCTSTR FilePath() const { return m_strPath; }

	// Read. The 2-argument overloads use the central defaults table (see GameConfig.cpp);
	// the 3-argument overloads take an explicit default, matching CWinApp::GetProfileXxx.
	int     GetInt(LPCTSTR lpszSection, LPCTSTR lpszKey) const;
	int     GetInt(LPCTSTR lpszSection, LPCTSTR lpszKey, int nDefault) const;
	CString GetString(LPCTSTR lpszSection, LPCTSTR lpszKey) const;
	CString GetString(LPCTSTR lpszSection, LPCTSTR lpszKey, LPCTSTR lpszDefault) const;

	// Write-through: values hit config.ini immediately (no explicit Save needed), matching
	// the old WriteProfileXxx semantics.
	void    SetInt(LPCTSTR lpszSection, LPCTSTR lpszKey, int nValue);
	void    SetString(LPCTSTR lpszSection, LPCTSTR lpszKey, LPCTSTR lpszValue);

private:
	CGameConfig();
	void EnsureInit() const;

	CString m_strPath;
};

// Convenience accessor used across both apps.
#define g_Config (CGameConfig::Instance())

#endif // !defined __GAMECONFIG_H
