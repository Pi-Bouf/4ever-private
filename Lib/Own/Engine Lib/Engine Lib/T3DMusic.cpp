#include "StdAfx.h"
#include "TMiniAudio.h"

BYTE CT3DMusic::m_bMasterVolume = VOLUME_MAX;
BYTE CT3DMusic::m_bON = TRUE;


CT3DMusic::CT3DMusic()
{
	m_strFILE.Empty();
	m_pSOUND = NULL;
	m_dwSEEK = 0;

	m_bFadeVolume = VOLUME_MAX;
	m_bVolume = VOLUME_MAX;
	m_bSTATE = MMS_CLOSED;
	m_bLOOP = FALSE;
}

CT3DMusic::~CT3DMusic()
{
	Unlock();
}

BYTE CT3DMusic::InitMusic( LPTSTR szFile)
{
	m_strFILE = szFile;
	return TRUE;
}

BYTE CT3DMusic::Lock()
{
	Unlock();

	if(!CTachyonMedia::m_pENGINE)
		return FALSE;
	m_pSOUND = new ma_sound;

	if( ma_sound_init_from_file(
		CTachyonMedia::m_pENGINE,
		LPCTSTR(m_strFILE),
		MA_SOUND_FLAG_STREAM|MA_SOUND_FLAG_NO_SPATIALIZATION,
		NULL, NULL,
		m_pSOUND) != MA_SUCCESS )
	{
		delete m_pSOUND;
		m_pSOUND = NULL;

		return FALSE;
	}
	m_bSTATE = MMS_OPEN;

	return TRUE;
}

void CT3DMusic::Unlock()
{
	if(m_pSOUND)
	{
		ma_sound_uninit(m_pSOUND);
		delete m_pSOUND;
		m_pSOUND = NULL;
	}

	m_bSTATE = MMS_CLOSED;
}

BYTE CT3DMusic::Play()
{
	if( !CTachyonMedia::m_bON || !m_bON )
		return FALSE;

	if( m_bSTATE == MMS_PAUSE )
	{
		if( ma_sound_start(m_pSOUND) != MA_SUCCESS )
		{
			Unlock();
			return FALSE;
		}
		m_bSTATE = MMS_PLAY;

		return TRUE;
	}

	if(!Lock())
		return FALSE;

	if(m_dwSEEK)
	{
		ma_uint32 nSampleRate = 0;

		ma_sound_get_data_format( m_pSOUND, NULL, NULL, &nSampleRate, NULL, 0);
		ma_sound_seek_to_pcm_frame( m_pSOUND, ma_uint64(m_dwSEEK) * nSampleRate / 1000);
	}

	ma_sound_set_looping( m_pSOUND, m_bLOOP ? MA_TRUE : MA_FALSE);
	ResetVolume();

	if( ma_sound_start(m_pSOUND) != MA_SUCCESS )
	{
		Unlock();
		return FALSE;
	}
	m_bSTATE = MMS_PLAY;

	return TRUE;
}

BYTE CT3DMusic::Stop()
{
	Unlock();
	return TRUE;
}

BYTE CT3DMusic::Pause()
{
	if( m_bSTATE == MMS_PLAY )
	{
		if( ma_sound_stop(m_pSOUND) != MA_SUCCESS )
		{
			Unlock();
			return FALSE;
		}

		m_bSTATE = MMS_PAUSE;
	}

	return TRUE;
}

BYTE CT3DMusic::ResetVolume()
{
	if(!m_pSOUND)
		return TRUE;

	FLOAT fVolume = CTachyonMedia::m_bBACK ? 0.0f : FLOAT(CTachyonMedia::m_bMasterVolume) *
		FLOAT(m_bMasterVolume) * FLOAT(m_bFadeVolume) * FLOAT(m_bVolume) /
		FLOAT(VOLUME_MAX * VOLUME_MAX * VOLUME_MAX * VOLUME_MAX);
	ma_sound_set_volume( m_pSOUND, fVolume);

	return TRUE;
}

BYTE CT3DMusic::SetPos( DWORD dwPOS)
{
	BYTE bPLAY = m_bSTATE == MMS_PLAY ? TRUE : FALSE;

	if( m_bSTATE == MMS_PLAY || m_bSTATE == MMS_PAUSE )
		Stop();
	m_dwSEEK = dwPOS;

	if(bPLAY)
		Play();

	return TRUE;
}

DWORD CT3DMusic::GetLength()
{
	float fLength = 0.0f;

	if( !m_pSOUND || ma_sound_get_length_in_seconds( m_pSOUND, &fLength) != MA_SUCCESS )
		return 0;

	return DWORD(fLength * 1000.0f);
}

DWORD CT3DMusic::GetPos()
{
	float fCursor = 0.0f;

	if( !m_pSOUND || ma_sound_get_cursor_in_seconds( m_pSOUND, &fCursor) != MA_SUCCESS )
		return m_dwSEEK;

	return DWORD(fCursor * 1000.0f);
}

BYTE CT3DMusic::IsPlay()
{
	// The music reached its end: the next Play() starts from the beginning.
	if( m_bSTATE == MMS_PLAY && !ma_sound_is_playing(m_pSOUND) )
	{
		m_bSTATE = MMS_OPEN;
		m_dwSEEK = 0;
	}

	return m_bSTATE == MMS_PLAY ? TRUE : FALSE;
}
