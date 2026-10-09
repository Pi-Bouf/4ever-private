#pragma once

// Keyboard and mouse queries of the platform layer. Every OS call for key
// states, the cursor position and the scan code mapping goes through here so
// the SDL3 port only has to replace this file. Key codes are Windows VK_
// values (the key settings store them) and points are in the client area of
// the main window.
class CTachyonInput
{
public:
	static HWND m_hWND;

public:
	static void SetWindow( HWND hWND);

	static BYTE IsKeyDown( BYTE bVKey);
	static BYTE IsCtrlDown();
	static BYTE IsAltDown();
	static BYTE IsShiftDown();
	static BYTE IsWinDown();
	static BYTE IsCapsLock();

	static CPoint GetCursorPos();
	static void SetCursorPos( CPoint point);
	static void ShowCursor( BOOL bShow);

	static WORD ScanToVKey( UINT nFlags);
	static CHAR ScanToChar( UINT nFlags);
};
