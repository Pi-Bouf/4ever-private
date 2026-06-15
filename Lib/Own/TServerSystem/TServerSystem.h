#pragma once

#define PRESENTATION_COLOR 14
#define SERVER_NAME_COLOR 6
#define VERSION_COLOR 11
#define LOG_INFO_COLOR 10
#define LOG_ERROR_COLOR 4
#define LOG_EVENT_COLOR 6
#define INI_PRESENTATION_COLOR 11
#define INI_VALUES_COLOR 7

#define TCONTROL_VERSION "1.1.0"
#define TLOGIN_VERSION "1.1.0"
#define TMAP_VERSION "1.1.0"
#define TPATCH_VERSION "1.1.0"
#define TRELAY_VERSION "1.1.0"
#define TWORLD_VERSION "1.1.0"


#define _CRT_SECURE_NO_WARNINGS

#include <ctime> 
#include <iostream>
#include <Windows.h>
#include <string>
#include <vector>
#include <cstdio>
#include <SimpleIni.h>
#include <ErrorCode.h>
#include <fstream>
#include <iomanip>
#include <format>

using namespace std;

class TServerSystem
{
public:
	TServerSystem(string ServerName, string ServerVersion);
	~TServerSystem();
	void StartServer();
	void ExitServer();

protected:
	char* GetLocalTime();
	char* GetLocalDate(size_t opt);
	void SSLogInfo(const char* fmt, ...);
	void SSLogEvent(const char *fmt, ...);
	void SSLogError(const char* fmt, ...);
	void DisplayIni();
	void LoadIntFromIni(string confName, int defaultValue, int& ref);
	void LoadStringFromIni(string confName, string defaultValue, string& ref);
	virtual DWORD OnEnter();
	virtual void OnExit();
	void StartupError(DWORD result);

private:
	string ServerName;
	string ServerNameOriginal;
	string ServerVersion;
	string ConsoleTitle;
	HANDLE hConsole;
	vector<vector<string>> serverConfig;
	CSimpleIniA cSimpleIni;

	void DisplayPresentation();
	void SSLog(const char* fmt, va_list args);
	void SSOutputTextFile(string text, int color = -1);
};

