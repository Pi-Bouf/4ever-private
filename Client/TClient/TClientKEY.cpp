#include "StdAfx.h"
#include "TClient.h"


CTClientKEY::CTClientKEY()
{
	m_point = CPoint( 0, 0);
	m_dwTick = 0;

	ResetSTATE();
}

CTClientKEY::~CTClientKEY()
{
}

void CTClientKEY::ResetSTATE()
{
	memset( m_vSTATE, 0, TKEY_COUNT * sizeof(TKEY_STATE));
	memset( m_vTICK, 0, TKEY_COUNT * sizeof(DWORD));
}

WORD CTClientKEY::GetCurMOD()
{
	WORD wRES = 0;

	if( GetCTRL() )
		wRES |= TKEYMOD_CTRL;
	if( GetALT() )
		wRES |= TKEYMOD_ALT;
	if( GetSHIFT() )
		wRES |= TKEYMOD_SHIFT;

	return wRES;
}

BYTE CTClientKEY::GetCTRL()
{
	return CTachyonInput::IsCtrlDown();
}

BYTE CTClientKEY::GetALT()
{
	return CTachyonInput::IsAltDown();
}

BYTE CTClientKEY::GetSHIFT()
{
	return CTachyonInput::IsShiftDown();
}

BYTE CTClientKEY::GetWIN()
{
	return CTachyonInput::IsWinDown();
}

BYTE CTClientKEY::IsKeyDown(BYTE bVKey)
{
	return CTachyonInput::IsKeyDown(bVKey);
}