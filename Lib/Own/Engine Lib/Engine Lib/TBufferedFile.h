// TBufferedFile.h: read-only CFile with an in-memory read buffer.
//
// The resource index scans read thousands of 1-4 byte fields and skip large blocks with Seek.
// On a plain CFile every one of those is a kernel call; here Seek only moves a position and
// small reads are served from a 64KB buffer. Large reads bypass the buffer.
//
//////////////////////////////////////////////////////////////////////

#if !defined ___TBUFFEREDFILE_H
#define ___TBUFFEREDFILE_H

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#define TBUFFEREDFILE_SIZE			(64 * 1024)


class CTBufferedFile : public CFile
{
public:
	// Opened shareDenyNone: the default CFile share mode is exclusive, which would fail
	// while the startup prefetch thread (CTachyonRes::StartPrefetch) has the same file open.
	CTBufferedFile( LPCTSTR lpszFileName, UINT nOpenFlags)
		: CFile( lpszFileName, (nOpenFlags & ~0x70)|CFile::shareDenyNone)
	{
		m_pBUF = new BYTE[TBUFFEREDFILE_SIZE];
		m_llBufPOS = 0;
		m_nBufLEN = 0;
		m_llPOS = 0;
		m_llLENGTH = CFile::GetLength();
	}

	virtual ~CTBufferedFile()
	{
		delete[] m_pBUF;
	}

	virtual UINT Read( void *lpBuf, UINT nCount)
	{
		LPBYTE pDEST = (LPBYTE) lpBuf;
		UINT nTotal = 0;

		while( nCount > 0 && m_llPOS < m_llLENGTH )
		{
			if( m_llPOS >= m_llBufPOS && m_llPOS < m_llBufPOS + m_nBufLEN )
			{
				UINT nOffset = UINT(m_llPOS - m_llBufPOS);
				UINT nCopy = min( nCount, m_nBufLEN - nOffset);

				memcpy( pDEST, m_pBUF + nOffset, nCopy);
				pDEST += nCopy;
				nCount -= nCopy;
				nTotal += nCopy;
				m_llPOS += nCopy;
			}
			else if( nCount >= TBUFFEREDFILE_SIZE )
			{
				CFile::Seek( LONGLONG(m_llPOS), CFile::begin);
				UINT nRead = CFile::Read( pDEST, nCount);

				nTotal += nRead;
				m_llPOS += nRead;
				break;
			}
			else
			{
				CFile::Seek( LONGLONG(m_llPOS), CFile::begin);
				m_llBufPOS = m_llPOS;
				m_nBufLEN = CFile::Read( m_pBUF, TBUFFEREDFILE_SIZE);

				if(!m_nBufLEN)
					break;
			}
		}

		return nTotal;
	}

	virtual ULONGLONG Seek( LONGLONG lOff, UINT nFrom)
	{
		LONGLONG llBASE = 0;

		switch(nFrom)
		{
		case CFile::current	: llBASE = LONGLONG(m_llPOS); break;
		case CFile::end		: llBASE = LONGLONG(m_llLENGTH); break;
		}

		if( llBASE + lOff < 0 )
			AfxThrowFileException( CFileException::badSeek, -1, GetFileName());
		m_llPOS = ULONGLONG(llBASE + lOff);

		return m_llPOS;
	}

	virtual ULONGLONG GetPosition() const
	{
		return m_llPOS;
	}

	virtual ULONGLONG GetLength() const
	{
		return m_llLENGTH;
	}

	virtual void Write( const void *lpBuf, UINT nCount)
	{
		AfxThrowNotSupportedException();
	}

protected:
	LPBYTE m_pBUF;
	ULONGLONG m_llBufPOS;
	UINT m_nBufLEN;

	ULONGLONG m_llPOS;
	ULONGLONG m_llLENGTH;
};


#endif // !defined ___TBUFFEREDFILE_H
