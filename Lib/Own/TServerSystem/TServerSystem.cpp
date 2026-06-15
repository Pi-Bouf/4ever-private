#include "TServerSystem.h"


#define WRITE_LOGS_IN_FILE bool(false)

TServerSystem::TServerSystem(string _serverName, string _serverVersion)
	:ServerName(_serverName), ServerNameOriginal(_serverName), ServerVersion(_serverVersion), hConsole(GetStdHandle(STD_OUTPUT_HANDLE))
{
	transform(ServerName.begin(), ServerName.end(), ServerName.begin(), (int(*)(int))toupper);
	ConsoleTitle = ServerName + " Server";

	string iniLocation = ".\\Configurations\\" + ServerNameOriginal + "Svr.ini";
	cSimpleIni.SetUnicode();
	cSimpleIni.LoadFile(iniLocation.c_str());

	SetConsoleTitle(ConsoleTitle.c_str());

	static bool bPresent = true;
	if (bPresent)
	{
		DisplayPresentation();
	}
	bPresent = false;
}

TServerSystem::~TServerSystem()
{
}

void TServerSystem::SSOutputTextFile(string text, int color)
{
	char buffer[256];
	sprintf(buffer, "Logs\\LOG[%s][%s][%s].txt", ServerName.c_str(), GetLocalDate(0), GetLocalDate(1));

	static ofstream output(buffer);
	output << "[" << GetLocalTime() << "]" << text << endl;
}

void TServerSystem::DisplayPresentation()
{
	SetConsoleTextAttribute(hConsole, PRESENTATION_COLOR);
	cout << endl;
	cout << " #################################################################### " << endl;
	cout << " #         .o    .oooooo..o     .                                   # " << endl;
	cout << " #       .d88   d8P'    `Y8   .o8                                   # " << endl;
	cout << " #     .d'888   Y88bo.      .o888oo  .ooooo.  oooo d8b oooo    ooo  # " << endl;
	cout << " #   .d'  888    `\"Y8888o.    888   d88' `88b `888\"\"8P  `88.  .8'   # " << endl;
	cout << " #   88ooo888oo      `\"Y88b   888   888   888  888       `88..8'    # " << endl;
	cout << " #        888   oo     .d8P   888 . 888   888  888        `888'     # " << endl;
	cout << " #       o888o  8\"\"88888P'    \"888\" `Y8bod8P' d888b        .8'      # " << endl;
	SetConsoleTextAttribute(hConsole, SERVER_NAME_COLOR);
	cout << " #       " << ServerName << " SERVER";
	SetConsoleTextAttribute(hConsole, PRESENTATION_COLOR);
	for (int i = 0; i < 39 - ServerName.length(); i++)
	{
		cout << " ";
	}
	cout << ".o..P'       # " << endl;
	SetConsoleTextAttribute(hConsole, VERSION_COLOR);
	cout << " #       VERSION: " << ServerVersion;
	SetConsoleTextAttribute(hConsole, PRESENTATION_COLOR);
	for (int i = 0; i < 37 - ServerVersion.length(); i++)
	{
		cout << " ";
	}
	cout << "`Y8P'        # " << endl;
	SetConsoleTextAttribute(hConsole, 9);
	cout << " #       RAGEZONE: https://forum.ragezone.com/community/4story.986";
	SetConsoleTextAttribute(hConsole, PRESENTATION_COLOR);
	cout << "  #" << endl;
	cout << " #################################################################### " << endl;
}

char* TServerSystem::GetLocalTime()
{
	static char timestamp[16];
	time_t timeNow = time(0);
	strftime(timestamp, 16, "%H:%M:%S", localtime(&timeNow));
	return timestamp;
}

char* TServerSystem::GetLocalDate(size_t opt)
{
	time_t timeNow = time(0);
	char timestamp[64] = "";

	if(opt == 0)
		strftime(timestamp, 64, "DA-%d MO-%m YE-%Y", localtime(&timeNow));
	else
		strftime(timestamp, 64, "HO-%H MI-%M SE-%S", localtime(&timeNow));

	return timestamp;
}

void TServerSystem::SSLog(const char* fmt, va_list args)
{
	char formatted[1024 * 5];
	vsprintf(formatted, fmt, args);

	cout << "[" << GetLocalTime() << "] >> " << formatted << endl;

	if (WRITE_LOGS_IN_FILE)
		SSOutputTextFile(formatted);
}

void TServerSystem::SSLogInfo(const char* fmt, ...)
{
	SetConsoleTextAttribute(hConsole, LOG_INFO_COLOR);

	va_list args;
	va_start(args, fmt);

	SSLog(fmt, args);

	va_end(args);
}

void TServerSystem::SSLogEvent(const char *fmt, ...)
{
	SetConsoleTextAttribute(hConsole, LOG_EVENT_COLOR);

	va_list args;
	va_start(args, fmt);

	SSLog(fmt, args);

	va_end(args);
}

void TServerSystem::SSLogError(const char* fmt, ...)
{
	SetConsoleTextAttribute(hConsole, LOG_ERROR_COLOR);
	
	va_list args;
	va_start(args, fmt);

	SSLog(fmt, args);

	va_end(args);
}

void TServerSystem::DisplayIni()
{
	SSLogInfo("Loaded configuration from %sSvr.ini:", ServerNameOriginal.c_str());
	SetConsoleTextAttribute(hConsole, INI_PRESENTATION_COLOR);
	cout << "##########################################" << endl;

	for (int i = 0; i < serverConfig.size(); ++i)
	{
		vector <string> currentConf = serverConfig.at(i);
		SetConsoleTextAttribute(hConsole, INI_PRESENTATION_COLOR);
		cout << "# + " << currentConf.at(0);
		for (int i = 0; i < 13 - currentConf.at(0).length(); i++)
		{
			cout << " ";
		}
		SetConsoleTextAttribute(hConsole, INI_VALUES_COLOR);
		cout << currentConf.at(1) << endl;
	}

	SetConsoleTextAttribute(hConsole, INI_PRESENTATION_COLOR);
	cout << "##########################################" << endl;
}

void TServerSystem::LoadIntFromIni(string confName, int defaultValue, int& ref)
{
	string namespaceIni = ServerNameOriginal + "Config";
	ref = stoi(cSimpleIni.GetValue(namespaceIni.c_str(), confName.c_str(), to_string(defaultValue).c_str()));
	vector <string> data;
	data.push_back(confName);
	data.push_back(to_string(ref));

	serverConfig.push_back(data);
}

void TServerSystem::LoadStringFromIni(string confName, string defaultValue, string& ref)
{
	string namespaceIni = ServerNameOriginal + "Config";
	ref = cSimpleIni.GetValue(namespaceIni.c_str(), confName.c_str(), defaultValue.c_str());
	vector <string> data;
	data.push_back(confName);
	data.push_back(ref);

	serverConfig.push_back(data);
}

DWORD TServerSystem::OnEnter()
{
	return OnEnter();
}

void TServerSystem::OnExit()
{
	SSLogInfo("Exiting application!", 0);
}

void TServerSystem::StartServer()
{
	SSLogInfo("Starting up...", 0);

	DWORD result = OnEnter();
	if (result) {
		StartupError(result);
	}
}

void TServerSystem::ExitServer()
{
	OnExit();
}

void TServerSystem::StartupError(DWORD result)
{
	SSLogError("Startup error: ", 2);
	switch (result) {
	case EC_INITSERVICE_DBOPENFAILED: {
		SSLogError("Can't connect to the database !", 0);
		SSLogError("1) Check ODBC x32 or x64, if all is configured", 0);
		SSLogError("2) Bad credentials in your .ini configuration file", 0);
		SSLogError("3) Check if you SQL Server is running !", 0);

		break;
	}
	case EC_INITSERVICE_CONNECTWORLD: {
		SSLogError("Can't connect to TWorld server !", 0);
		SSLogError("1) Check if TWorldSvr is running", 0);
		SSLogError("2) Check your .ini configuration file", 0);
	}
	default:
		SSLogError("Unknown error !", 0);
	}

	SSLogError("Can't start the server...", 2);
}