// D3DSound.cpp: implementation of the CD3DSound class.
//
//////////////////////////////////////////////////////////////////////

#include "stdafx.h"
#include "TMiniAudio.h"

VTSOUND CD3DSound::m_vGARBAGE;
D3DXMATRIX CD3DSound::m_vLISTENER;

BYTE CD3DSound::m_bMasterVolume = VOLUME_MAX;
BYTE CD3DSound::m_bON = TRUE;


// Same 3D model as the DirectSound buffers this class used: head-relative positions (see
// ConvertPOS) with the default distances, so the gain is 1 / distance past 1 unit.
static void InitSound( ma_sound *pSOUND)
{
	ma_sound_set_positioning( pSOUND, ma_positioning_relative);
	ma_sound_set_attenuation_model( pSOUND, ma_attenuation_model_inverse);
	ma_sound_set_min_distance( pSOUND, 1.0f);
	ma_sound_set_rolloff( pSOUND, 1.0f);
	ma_sound_set_doppler_factor( pSOUND, 0.0f);
}

static void FreeSound( ma_sound *pSOUND)
{
	ma_sound_uninit(pSOUND);
	delete pSOUND;
}

// Size of one frame of the decoded data, the unit of GetPos, SetPos and GetLength.
static DWORD GetFrameSize( ma_sound *pSOUND)
{
	ma_uint32 nChannels = 0;
	ma_format nFormat;

	if( ma_sound_get_data_format( pSOUND, &nFormat, &nChannels, NULL, NULL, 0) != MA_SUCCESS )
		return 0;

	return ma_get_bytes_per_frame( nFormat, nChannels);
}


//////////////////////////////////////////////////////////////////////
// class CD3DSound
//////////////////////////////////////////////////////////////////////

CD3DSound::CD3DSound()
{
	m_vBUF.clear();
	m_vLOCK.clear();

	m_strFile.Empty();

	m_bFadeVolume = VOLUME_MAX;
	m_bVolume = VOLUME_MAX;
	m_dwSize = 0;
}

CD3DSound::~CD3DSound()
{
	Release();
}

void CD3DSound::Initialize( CString strFile)
{
	Release();
	m_strFile = strFile;
}

BYTE CD3DSound::LoadData()
{
	if(!CTachyonMedia::m_pENGINE)
		return FALSE;

	Release();
	ma_sound *pSOUND = new ma_sound;

	// Decoded once and shared with the copies made by Lock().
	if( ma_sound_init_from_file(
		CTachyonMedia::m_pENGINE,
		LPCTSTR(m_strFile),
		MA_SOUND_FLAG_DECODE,
		NULL, NULL,
		pSOUND) != MA_SUCCESS )
	{
		delete pSOUND;
		return FALSE;
	}

	ma_uint64 nLength = 0;
	ma_uint32 nChannels = 0;

	ma_sound_get_data_format( pSOUND, NULL, &nChannels, NULL, NULL, 0);
	ma_sound_get_length_in_pcm_frames( pSOUND, &nLength);

	// DirectSound has no 3D buffer for stereo data: those sounds were played without position.
	if( nChannels != 1 )
		ma_sound_set_spatialization_enabled( pSOUND, MA_FALSE);
	InitSound(pSOUND);

	m_vLOCK.push_back(FALSE);
	m_vBUF.push_back(pSOUND);
	m_dwSize = DWORD(nLength * GetFrameSize(pSOUND));

	ResetVolume();

	return TRUE;
}

ma_sound *CD3DSound::CopySound()
{
	ma_sound *pSOUND = new ma_sound;

	if( ma_sound_init_copy(
		CTachyonMedia::m_pENGINE,
		m_vBUF[0],
		ma_sound_is_spatialization_enabled(m_vBUF[0]) ? 0 : MA_SOUND_FLAG_NO_SPATIALIZATION,
		NULL,
		pSOUND) != MA_SUCCESS )
	{
		delete pSOUND;
		return NULL;
	}
	InitSound(pSOUND);

	return pSOUND;
}

void CD3DSound::Release( ma_sound *pSOUND)
{
	// A released sound plays to its end.
	if(IsPlay(pSOUND))
		m_vGARBAGE.push_back(pSOUND);
	else
		FreeSound(pSOUND);
}

void CD3DSound::Release()
{
	for( int i=0; i<INT(m_vBUF.size()); i++)
		if(m_vBUF[i])
			Release(m_vBUF[i]);

	m_vBUF.clear();
	m_vLOCK.clear();
	m_dwSize = 0;
}

BYTE CD3DSound::SetPosition( int nIndex,
							 FLOAT fPosX,
							 FLOAT fPosY,
							 FLOAT fPosZ)
{
	if(!Is3D(nIndex))
		return FALSE;

	// DirectSound is left-handed (+Z in front of the listener), miniaudio's listener looks down -Z.
	ma_sound_set_position( m_vBUF[nIndex], fPosX, fPosY, -fPosZ);

	return TRUE;
}

BYTE CD3DSound::Is3D( int nIndex)
{
	if( nIndex < 0 || nIndex >= INT(m_vBUF.size()) || !m_vBUF[nIndex] )
		return FALSE;

	return ma_sound_is_spatialization_enabled(m_vBUF[nIndex]) ? TRUE : FALSE;
}

BYTE CD3DSound::ResetVolume( int nIndex)
{
	if( nIndex < 0 || nIndex >= INT(m_vBUF.size()) || !m_vBUF[nIndex] )
		return FALSE;

	FLOAT fVolume = CTachyonMedia::m_bBACK ? 0.0f : FLOAT(CTachyonMedia::m_bMasterVolume) *
		FLOAT(m_bMasterVolume) * FLOAT(m_bFadeVolume) * FLOAT(m_bVolume) /
		FLOAT(VOLUME_MAX * VOLUME_MAX * VOLUME_MAX * VOLUME_MAX);
	ma_sound_set_volume( m_vBUF[nIndex], fVolume);

	return TRUE;
}

BYTE CD3DSound::ResetVolume()
{
	for( int i=0; i<INT(m_vBUF.size()); i++)
		if(m_vLOCK[i])
			ResetVolume(i);

	return TRUE;
}

BYTE CD3DSound::SetPos( int nIndex, DWORD dwPos)
{
	if( nIndex < 0 || nIndex >= INT(m_vBUF.size()) || !m_vBUF[nIndex] )
		return FALSE;

	DWORD dwFrameSize = GetFrameSize(m_vBUF[nIndex]);
	if(!dwFrameSize)
		return FALSE;

	return ma_sound_seek_to_pcm_frame( m_vBUF[nIndex], dwPos / dwFrameSize) == MA_SUCCESS ? TRUE : FALSE;
}

DWORD CD3DSound::GetPos( int nIndex)
{
	if( nIndex < 0 || nIndex >= INT(m_vBUF.size()) || !m_vBUF[nIndex] )
		return 0;

	ma_uint64 nCursor = 0;
	if( ma_sound_get_cursor_in_pcm_frames( m_vBUF[nIndex], &nCursor) != MA_SUCCESS )
		return 0;

	return DWORD(nCursor * GetFrameSize(m_vBUF[nIndex]));
}

DWORD CD3DSound::GetLength()
{
	return m_dwSize;
}

BYTE CD3DSound::Pause( int nIndex)
{
	if( nIndex < 0 || nIndex >= INT(m_vBUF.size()) || !m_vBUF[nIndex] )
		return FALSE;

	return ma_sound_stop(m_vBUF[nIndex]) == MA_SUCCESS ? TRUE : FALSE;
}

BYTE CD3DSound::Stop( int nIndex)
{
	if( nIndex < 0 || nIndex >= INT(m_vBUF.size()) || !m_vBUF[nIndex] )
		return FALSE;

	if( ma_sound_stop(m_vBUF[nIndex]) != MA_SUCCESS )
		return FALSE;
	ma_sound_seek_to_pcm_frame( m_vBUF[nIndex], 0);

	return TRUE;
}

BYTE CD3DSound::Play( int nIndex)
{
	if( !CTachyonMedia::m_bON || !m_bON )
		return FALSE;

	if( nIndex < 0 || nIndex >= INT(m_vBUF.size()) || !m_vBUF[nIndex] )
		return FALSE;
	ResetVolume(nIndex);

	// A sound that reached its end starts again from the beginning, like a DirectSound buffer.
	return ma_sound_start(m_vBUF[nIndex]) == MA_SUCCESS ? TRUE : FALSE;
}

void CD3DSound::Stop()
{
	for( int i=0; i<INT(m_vBUF.size()); i++)
		if(m_vBUF[i])
			FreeSound(m_vBUF[i]);

	m_vBUF.clear();
	m_vLOCK.clear();
}

int CD3DSound::Play()
{
	int nIndex = Lock();

	if( nIndex >= 0 )
	{
		if(!Play(nIndex))
		{
			Unlock(nIndex);
			return -1;
		}

		Unlock(nIndex);
	}

	return nIndex;
}

BYTE CD3DSound::IsPlay( int nIndex)
{
	return nIndex >= 0 && nIndex < INT(m_vBUF.size()) && m_vBUF[nIndex] ? IsPlay(m_vBUF[nIndex]) : FALSE;
}

BYTE CD3DSound::IsPlay( ma_sound *pSOUND)
{
	return ma_sound_is_playing(pSOUND) ? TRUE : FALSE;
}

void CD3DSound::Unlock( int nIndex)
{
	if( nIndex >= 0 && nIndex < INT(m_vBUF.size()) )
	{
		if( m_vLOCK[nIndex] && nIndex > 0 )
		{
			if(m_vBUF[nIndex])
				Release(m_vBUF[nIndex]);

			m_vBUF[nIndex] = NULL;
		}
		m_vLOCK[nIndex] = FALSE;

		for( int i=0; i<INT(m_vLOCK.size()); i++)
			if(m_vLOCK[i])
				return;

		Release();
	}
}

int CD3DSound::Lock()
{
	if( !CTachyonMedia::m_pENGINE || !CTachyonMedia::m_bON || !m_bON )
		return -1;

	if(m_vBUF.empty())
	{
		if(!LoadData())
			return -1;
		m_vLOCK[0] = TRUE;

		return 0;
	}

	for( int i=0; i<INT(m_vLOCK.size()); i++)
		if(!m_vLOCK[i])
		{
			if(!m_vBUF[i])
			{
				m_vBUF[i] = CopySound();

				if(!m_vBUF[i])
					return -1;
			}
			m_vLOCK[i] = TRUE;

			return i;
		}

	ma_sound *pSOUND = CopySound();
	if(!pSOUND)
		return -1;

	m_vBUF.push_back(pSOUND);
	m_vLOCK.push_back(TRUE);

	return INT(m_vBUF.size()) - 1;
}

void CD3DSound::ResetLISTENER( LPD3DXVECTOR3 pPosition,
							   LPD3DXVECTOR3 pAxisZ,
							   LPD3DXVECTOR3 pAxisY)
{
	D3DXMATRIX vSCALE(
		0.25f, 0.0f, 0.0f, 0.0f,
		0.0f, 0.25f, 0.0f, 0.0f,
		0.0f, 0.0f, 0.25f, 0.0f,
		0.0f, 0.0f, 0.0f, 1.0f);

	D3DXMatrixLookAtLH(
		&m_vLISTENER,
		pPosition,
		TTEMP(((*pPosition) + (*pAxisZ))),
		pAxisY);
	m_vLISTENER *= vSCALE;

	VTSOUND::iterator it = m_vGARBAGE.begin();
	while(it != m_vGARBAGE.end())
		if(!IsPlay(*it))
		{
			FreeSound(*it);
			it = m_vGARBAGE.erase(it);
		}
		else
			it++;
}

void CD3DSound::ClearGARBAGE()
{
	while(!m_vGARBAGE.empty())
	{
		FreeSound(m_vGARBAGE.back());
		m_vGARBAGE.pop_back();
	}
}

void CD3DSound::InitGARBAGE()
{
	m_vGARBAGE.clear();
}

D3DXVECTOR3 CD3DSound::ConvertPOS( FLOAT fPosX,
								   FLOAT fPosY,
								   FLOAT fPosZ)
{
	D3DXVECTOR3 vResult(
		fPosX,
		fPosY,
		fPosZ);

	CTMath::Transform(
		&m_vLISTENER,
		&vResult);

	return vResult;
}
