// D3DSound.h: interface for the CD3DSound class.
//
//////////////////////////////////////////////////////////////////////

#if !defined __D3DSOUND_H
#define __D3DSOUND_H


#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000


class CD3DSound
{
	friend class CTachyonMedia;

public:
	static BYTE m_bMasterVolume;
	static BYTE m_bON;

protected:
	// Released sounds that were still playing, freed once they end.
	static VTSOUND m_vGARBAGE;
	static D3DXMATRIX m_vLISTENER;

public:
	static void ResetLISTENER(
		LPD3DXVECTOR3 pPosition,
		LPD3DXVECTOR3 pAxisZ,
		LPD3DXVECTOR3 pAxisY);

	static D3DXVECTOR3 ConvertPOS(
		FLOAT fPosX,
		FLOAT fPosY,
		FLOAT fPosZ);

	static BYTE IsPlay( ma_sound *pSOUND);
	static void ClearGARBAGE();
	static void InitGARBAGE();

protected:
	// One playing instance per slot: slot 0 is loaded from the file, the others are copies of it.
	VTSOUND m_vBUF;
	VECTORBYTE m_vLOCK;

	CString m_strFile;
	DWORD m_dwSize;

public:
	BYTE m_bFadeVolume;
	BYTE m_bVolume;

protected:
	ma_sound *CopySound();
	void Release( ma_sound *pSOUND);

public:
	void Initialize( CString strFile);

	BYTE LoadData();
	void Release();

public:
	BYTE SetPosition(
		int nIndex,
		FLOAT fPosX,
		FLOAT fPosY,
		FLOAT fPosZ);
	BYTE Is3D( int nIndex);

	BYTE ResetVolume( int nIndex);
	BYTE ResetVolume();

	DWORD GetPos( int nIndex);
	DWORD GetLength();

	BYTE SetPos( int nIndex, DWORD dwPos);
	BYTE IsPlay( int nIndex);

	BYTE Pause( int nIndex);
	BYTE Stop( int nIndex);
	BYTE Play( int nIndex);

	void Stop();
	int Play();

	void Unlock( int nIndex);
	int Lock();

public:
	CD3DSound();
	virtual ~CD3DSound();
};


#endif // !defined __D3DSOUND_H
