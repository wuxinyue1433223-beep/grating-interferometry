// SC30_C++_Demo.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include "TestFunctions.h""

DeviceMap _sc30_deviceMap;

char errStr[STRING_LENGTH_512];

char str[STRING_LENGTH_512];
char str1[STRING_LENGTH_512];
int strLength = STRING_LENGTH_512;
int strLength1 = STRING_LENGTH_512;

char c_szPath[STRING_LENGTH_512];

int numModules = 0;

ModuleHandle hSC30_Module = NULL;

DeviceHandle *deviceHandles = NULL;
int numDevices = 0;

DeviceHandle hSC30_Device = NULL;

ArrayHandle hFriendlyName = NULL;
int friendlyNameArraySize1 = 0;

bool isDeviceOpen = false;
bool isThreadTerminated = true;

int reportSize = 0;

int main()
{
	std::cout << "Thorlabs Device SDK Demo Program" << std::endl;
	std::cout << std::endl;

	SDKErrChk(OpenPluginManager(), NULL);

	SDKErrChk(GetSDKPropertyString(_SDKProperty::SDK_MajorVersion, str, &strLength), NULL);

	strLength = STRING_LENGTH_512;
	SDKErrChk(GetSDKPropertyString(_SDKProperty::SDK_MinorVersion, str1, &strLength), NULL);

	std::cout << "SDK Version: " << str << "." << str1 << std::endl;

	strLength = STRING_LENGTH_512;
	SDKErrChk(GetAppDirectory(str, &strLength), NULL);
	std::cout << "Software directory: " << str << std::endl;

	GetModuleAndDevice();

	if (hSC30_Module == NULL ||
		hSC30_Device == NULL)
	{
		std::cout << "\nNo SC30 is found. Test is over. \nPress any key to exit the program...";
		getchar();

		return Rtn_OK;
	}

	std::cout << "\nOpening SC30 device...";
	isDeviceOpen = OpenDevice(hSC30_Device);
	ErrChk("OpenDevice", hSC30_Device);

	if (!isDeviceOpen)
	{
		std::cout << "Failed." << std::endl;
		std::cout << "Cannot open the SC30. Press any key to exit the testing...";
		getchar();
		return Rtn_ERROR;
	}
	else
	{
		std::cout << "Successful" << std::endl;
		std::cout << std::endl;
	}

	std::cout << "The following tests are available: \n\n";		
	std::cout << "n: System notification test\n";
	std::cout << "h: HyperTerminal command test\n";
	std::cout << "c: Comm Port Callback test\n";
	std::cout << "p: parameters test\n";
	std::cout << "a: array test\n";
	std::cout << "f: profile test\n";	
	std::cout << "x: Exit\n\nSelect a test: ";

	char c = tolower(getchar());		// Waiting for choice and enter
	getchar();							// read enter key

	switch (c)
	{
	case 'n':
	{
		SystemNotificationTest();
		break;
	}

	case 'h':
	{
		HyperTerminalTest();
		break;
	}

	case 'c':
	{
		CommPortCallbackTest();
		break;
	}

	case 'p':
	{
		ParameterTest();
		break;
	}

	case 'a':
	{
		ArrayTest();
		break;
	}

	case 'f':
	{
		ProfileTest();
		break;
	}

	case 'x':
		break;

	default:
		std::cerr << "Function key not supported!" << std::endl;
		break;
	}

	if (isDeviceOpen)
	{
		SDKErrChk(CloseDevice(hSC30_Device), hSC30_Device);
		isDeviceOpen = false;
	}

	//SDKErrChk(ClosePluginManager(), NULL);

	std::cout << "\nTest is complete. \nPress any key to exit the program...";
	getchar();

	return Rtn_OK;
}