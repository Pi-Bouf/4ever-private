#pragma once


// Background music (the MP3 files of Data\Media), streamed from the file while it plays.
class CT3DMusic
{
public:
	static BYTE m_bMasterVolume;
	static BYTE m_bON;

private:
	ma_sound *m_pSOUND;

	// Where the next Play() starts, in milliseconds.
	DWORD m_dwSEEK;

private:
	void Unlock();
	BYTE Lock();

public:
	BYTE SetPos( DWORD dwPOS);

	DWORD GetLength();
	DWORD GetPos();

public:
	BYTE InitMusic( LPTSTR szFile);
	BYTE ResetVolume();

	BYTE IsPlay();
	BYTE Pause();
	BYTE Play();
	BYTE Stop();

public:
	CString m_strFILE;

	BYTE m_bFadeVolume;
	BYTE m_bVolume;
	BYTE m_bSTATE;
	BYTE m_bLOOP;

public:
	CT3DMusic();
	virtual ~CT3DMusic();
};
