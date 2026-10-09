#include "StdAfx.h"


HWND CTachyonInput::m_hWND = NULL;

void CTachyonInput::SetWindow( HWND hWND)
{
	m_hWND = hWND;
}

BYTE CTachyonInput::IsKeyDown( BYTE bVKey)
{
	return GetAsyncKeyState(bVKey) < 0 ? TRUE : FALSE;
}

BYTE CTachyonInput::IsCtrlDown()
{
	return GetKeyState(VK_CONTROL) < 0 ? TRUE : FALSE;
}

BYTE CTachyonInput::IsAltDown()
{
	return GetKeyState(VK_MENU) < 0 ? TRUE : FALSE;
}

BYTE CTachyonInput::IsShiftDown()
{
	return GetKeyState(VK_SHIFT) < 0 ? TRUE : FALSE;
}

BYTE CTachyonInput::IsWinDown()
{
	return GetKeyState(VK_LWIN) < 0 || GetKeyState(VK_RWIN) < 0 ? TRUE : FALSE;
}

BYTE CTachyonInput::IsCapsLock()
{
	return GetKeyState(VK_CAPITAL) & 0x0001 ? TRUE : FALSE;
}

CPoint CTachyonInput::GetCursorPos()
{
	CPoint point( 0, 0);

	::GetCursorPos(&point);
	if(m_hWND)
		::ScreenToClient( m_hWND, &point);

	return point;
}

void CTachyonInput::SetCursorPos( CPoint point)
{
	if(m_hWND)
		::ClientToScreen( m_hWND, &point);

	::SetCursorPos( point.x, point.y);
}

void CTachyonInput::ShowCursor( BOOL bShow)
{
	::ShowCursor(bShow);
}

// nFlags is the scan code word of a key message (HIWORD of lParam).
WORD CTachyonInput::ScanToVKey( UINT nFlags)
{
	return (WORD) MapVirtualKey( LOBYTE(nFlags), MAPVK_VSC_TO_VK);
}

CHAR CTachyonInput::ScanToChar( UINT nFlags)
{
	BYTE bUpper = IsCapsLock();

	if(IsShiftDown())
		bUpper = !bUpper;

	CHAR nChar[2] = {
		CHAR(ScanToVKey(nFlags)),
		NULL};

	if(!bUpper)
		_strlwr_s(nChar);

	return nChar[0];
}
