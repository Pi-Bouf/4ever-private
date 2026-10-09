#pragma once

#define ON_TRECEIVE(x)							case x	: nERROR = On##x( pSession, pPacket); break;
#define SESSIONBUF_SIZE							(1024 * 64)
#define KEY_COUNT								((BYTE) 7)

// Longest wait for a window message while the client is inactive, so the sessions keep being
// polled (the network used to wake WaitMessage() through WSAAsyncSelect messages).
#define TSESSION_IDLE_WAIT						10

// Most reads done for one session in one PollAll(), so a flood cannot starve the frame.
#define TSESSION_MAX_READS						32

class CTachyonSession
{
public:
	static MAPSESSION m_mapSESSION;

	static CTachyonSession *GetSession( SOCKET sock);
	static void ReleaseTachyonSession();
	static void InitTachyonSession();

	// Polls every session for connect completion, incoming data and close, and calls
	// OnConnect/OnReceive/OnClose. Called once per main loop iteration.
	static void PollAll();

public:
	CTachyonWnd *m_pOwner;
	CPacket m_packet;

	SOCKADDR_IN m_target;
	SOCKET m_sock;
	BYTE m_bConnecting;
	BYTE m_bValid;
	BYTE m_bLock;
	BYTE m_bValidLogin;

	DWORD m_dwSendNumber;
	DWORD m_dwRecvNumber;

	int m_nCS;
	int m_nPacketCount;
	int m_nPacketCount_PF;

	WORD m_wLastSend;

public:
	void SetOwner( CTachyonWnd *pOwner);

	BYTE Start( LPCTSTR strAddr, DWORD dwPort, BYTE bType = SOCK_STREAM);
	BYTE Read( DWORD dwRead);
	BYTE IsValid();

	void Encrypt( CPacket *pPacket);

	void Say( CPacket *pPacket);
	void Flush();
	void End();

	int CheckMSG();

protected:
	void Poll();

public:
	virtual BOOL OnReceive( int nErrorCode);

	virtual void OnConnect( int nErrorCode);
	virtual void OnClose( int nErrorCode);

public:
	CTachyonSession();
	~CTachyonSession();
};
