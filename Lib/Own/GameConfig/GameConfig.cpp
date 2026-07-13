// GameConfig.cpp : implementation of the CGameConfig class.
//
// Compiled with precompiled headers DISABLED (see the .vcxproj entries) so it can be shared
// verbatim by TClient and TLauncher, which have different stdafx.h files.
//
//////////////////////////////////////////////////////////////////////

#include <afx.h>
#include <tchar.h>
#include "GameConfig.h"

//////////////////////////////////////////////////////////////////////
// Central defaults table -- the single source of truth for every fixed setting. Used by the
// 2-argument getters (and therefore by the launcher's settings dialogs and any fresh install).
// Note: .ini section/key names are case-insensitive, so the client's "TextureDetail" and the
// launcher's "TextureDETAIL" resolve to the same entry -- that is intentional (the launcher's
// dialogs are meant to configure the client).
//////////////////////////////////////////////////////////////////////

struct TConfigDefaultInt  { LPCTSTR lpszSection; LPCTSTR lpszKey; int     nDefault; };
struct TConfigDefaultStr  { LPCTSTR lpszSection; LPCTSTR lpszKey; LPCTSTR lpszDefault; };

static const TConfigDefaultInt s_DefaultInts[] =
{
	// [Settings] -- display
	{ _T("Settings"), _T("ScreenX"),               1024 },
	{ _T("Settings"), _T("ScreenY"),               768  },
	{ _T("Settings"), _T("SwapEffect"),            0    },
	{ _T("Settings"), _T("PresentationInterval"),  0    },
	{ _T("Settings"), _T("Behavior"),              0    },
	{ _T("Settings"), _T("TextureDetail"),         1    },
	// [Settings] -- graphics quality
	{ _T("Settings"), _T("MapDETAIL"),             1    },
	{ _T("Settings"), _T("ObjDETAIL"),             1    },
	{ _T("Settings"), _T("MapSHADOW"),             1    },
	{ _T("Settings"), _T("ObjSHADOW"),             1    },
	{ _T("Settings"), _T("MapSFX"),                1    },
	{ _T("Settings"), _T("SfxDETAIL"),             1    },
	{ _T("Settings"), _T("DLIGHTMAP"),             1    },
	{ _T("Settings"), _T("FLIGHTMAP"),             1    },
	{ _T("Settings"), _T("FarIMAGE"),              1    },
	// [Settings] -- audio
	{ _T("Settings"), _T("MASTER"),                1    },
	{ _T("Settings"), _T("MainVolume"),            100  },
	{ _T("Settings"), _T("BGM"),                   1    },
	{ _T("Settings"), _T("BGMVolume"),             100  },
	{ _T("Settings"), _T("SOUND"),                 1    },
	{ _T("Settings"), _T("SFXVolume"),             100  },
	// [Settings] -- gameplay / UI toggles
	{ _T("Settings"), _T("NpcNAME"),               1    },
	{ _T("Settings"), _T("MonNAME"),               1    },
	{ _T("Settings"), _T("PcNAME"),                1    },
	{ _T("Settings"), _T("AUTOHELP"),              1    },
	{ _T("Settings"), _T("TALKBOX"),               1    },
	{ _T("Settings"), _T("HUD"),                   1    },
	{ _T("Settings"), _T("DENYWHI"),               0    },
	{ _T("Settings"), _T("DENYCOM"),               0    },
	{ _T("Settings"), _T("CONCHAT"),               0    },
	{ _T("Settings"), _T("MOUSECLICKMOVE"),        0    },
	{ _T("Settings"), _T("AUTOTARGETING"),         1    },
	{ _T("Settings"), _T("FontSize"),              100  },
	{ _T("Settings"), _T("FIRST"),                 1    },

	// [Launcher] -- patch config + flags
	{ _T("Launcher"), _T("version"),               0    },
	{ _T("Launcher"), _T("port"),                  0    },
	{ _T("Launcher"), _T("PrePatchFirst"),         0    },
	{ _T("Launcher"), _T("PrePatchAutoStart"),     0    },
};

static const TConfigDefaultStr s_DefaultStrs[] =
{
	// [Settings]
	{ _T("Settings"), _T("WindowedMode"),  _T("FALSE") },
	{ _T("Settings"), _T("UseShader"),     _T("TRUE")  },
	{ _T("Settings"), _T("OBJRange"),      _T("1.0")   },
	{ _T("Settings"), _T("Font"),          _T("")      },
	{ _T("Settings"), _T("FontQuality"),   _T("ANTIALIASED_QUALITY") },

	// [Launcher]
	{ _T("Launcher"), _T("directory"),     _T("")            },  // resolved to exe folder by the launcher
	{ _T("Launcher"), _T("exe"),           _T("TClient.exe") },
	{ _T("Launcher"), _T("address"),       _T("127.0.0.1")   },
	{ _T("Launcher"), _T("disclaimer"),    _T("FALSE")       },
};

static int LookupDefaultInt(LPCTSTR lpszSection, LPCTSTR lpszKey)
{
	for (int i = 0; i < sizeof(s_DefaultInts) / sizeof(s_DefaultInts[0]); ++i)
	{
		if (_tcsicmp(s_DefaultInts[i].lpszSection, lpszSection) == 0 &&
			_tcsicmp(s_DefaultInts[i].lpszKey,     lpszKey)     == 0)
			return s_DefaultInts[i].nDefault;
	}
	return 0;
}

static LPCTSTR LookupDefaultStr(LPCTSTR lpszSection, LPCTSTR lpszKey)
{
	for (int i = 0; i < sizeof(s_DefaultStrs) / sizeof(s_DefaultStrs[0]); ++i)
	{
		if (_tcsicmp(s_DefaultStrs[i].lpszSection, lpszSection) == 0 &&
			_tcsicmp(s_DefaultStrs[i].lpszKey,     lpszKey)     == 0)
			return s_DefaultStrs[i].lpszDefault;
	}
	return _T("");
}

//////////////////////////////////////////////////////////////////////
// CGameConfig
//////////////////////////////////////////////////////////////////////

CGameConfig& CGameConfig::Instance()
{
	static CGameConfig s_instance;
	return s_instance;
}

CGameConfig::CGameConfig()
{
}

void CGameConfig::Init()
{
	TCHAR szPath[MAX_PATH] = { 0 };
	::GetModuleFileName(NULL, szPath, MAX_PATH);

	LPTSTR pSlash = _tcsrchr(szPath, _T('\\'));
	if (pSlash)
		*(pSlash + 1) = _T('\0');

	m_strPath = szPath;
	m_strPath += _T("config.ini");
}

void CGameConfig::EnsureInit() const
{
	if (m_strPath.IsEmpty())
		const_cast<CGameConfig*>(this)->Init();
}

int CGameConfig::GetInt(LPCTSTR lpszSection, LPCTSTR lpszKey) const
{
	return GetInt(lpszSection, lpszKey, LookupDefaultInt(lpszSection, lpszKey));
}

int CGameConfig::GetInt(LPCTSTR lpszSection, LPCTSTR lpszKey, int nDefault) const
{
	EnsureInit();
	return (int)::GetPrivateProfileInt(lpszSection, lpszKey, nDefault, m_strPath);
}

CString CGameConfig::GetString(LPCTSTR lpszSection, LPCTSTR lpszKey) const
{
	return GetString(lpszSection, lpszKey, LookupDefaultStr(lpszSection, lpszKey));
}

CString CGameConfig::GetString(LPCTSTR lpszSection, LPCTSTR lpszKey, LPCTSTR lpszDefault) const
{
	EnsureInit();
	TCHAR szBuf[1024] = { 0 };
	::GetPrivateProfileString(lpszSection, lpszKey, lpszDefault ? lpszDefault : _T(""),
		szBuf, 1024, m_strPath);
	return CString(szBuf);
}

void CGameConfig::SetInt(LPCTSTR lpszSection, LPCTSTR lpszKey, int nValue)
{
	EnsureInit();
	CString strValue;
	strValue.Format(_T("%d"), nValue);
	::WritePrivateProfileString(lpszSection, lpszKey, strValue, m_strPath);
}

void CGameConfig::SetString(LPCTSTR lpszSection, LPCTSTR lpszKey, LPCTSTR lpszValue)
{
	EnsureInit();
	::WritePrivateProfileString(lpszSection, lpszKey, lpszValue, m_strPath);
}
