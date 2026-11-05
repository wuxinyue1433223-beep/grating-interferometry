#include "TestFunctions.h"

int HyperTerminalTest()
{
	std::cout << std::endl;
	std::cout << "This example is to demonstrate how to send HyperTerminal command to the Thorlabs devices." << std::endl;
	std::cout << std::endl;

	std::cout << "Opening the COMM port...";
	SDKErrChk(OpenComPort(hSC30_Device), hSC30_Device);
	std::cout << "Done" << std::endl;

	char RxData[MAX_COMMPORT_BUFFER];
	char TxCommand[MAX_COMMPORT_BUFFER];

	std::cout << std::endl;
	std::cout << "Sending command '?' to get the Command menu of the device...";

	TxCommand[0] = '?';
	TxCommand[1] = '\0';

	int BytesRead = MAX_COMMPORT_BUFFER;
	SDKErrChk(WriteComPort(hSC30_Device, TxCommand, RxData, &BytesRead), hSC30_Device);
	RxData[BytesRead] = NULL;

	std::cout << "Sent" << std::endl;
	std::cout << "The response is: ";

	for (int i = 0; i < BytesRead; i++)
	{
		std::cout << (char)RxData[i];
		if ((char)RxData[i] == '\r')
			std::cout << std::endl;
	}

	std::cout << std::endl;
	std::cout << std::endl;
	std::cout << "Sending command 'id?' to get the ID of the device...";

	TxCommand[0] = 'i';
	TxCommand[1] = 'd';
	TxCommand[2] = '?';
	TxCommand[3] = '\0';

	BytesRead = MAX_COMMPORT_BUFFER;
	SDKErrChk(WriteComPort(hSC30_Device, TxCommand, RxData, &BytesRead), hSC30_Device);
	RxData[BytesRead] = NULL;

	std::cout << "Sent" << std::endl;
	std::cout << "The response is: ";

	for (int i = 0; i < BytesRead; i++)
	{
		std::cout << (char)RxData[i];
		if ((char)RxData[i] == '\r')
			std::cout << std::endl;
	}

	std::cout << std::endl;
	std::cout << std::endl;
	std::cout << "Sending command 'friendly?' to get the friendly name...";

	TxCommand[0] = 'F';
	TxCommand[1] = 'R';
	TxCommand[2] = 'I';
	TxCommand[3] = 'E';
	TxCommand[4] = 'N';
	TxCommand[5] = 'D';
	TxCommand[6] = 'L';
	TxCommand[7] = 'Y';
	TxCommand[8] = '?';
	TxCommand[9] = '\0';

	BytesRead = MAX_COMMPORT_BUFFER;
	SDKErrChk(WriteComPort(hSC30_Device, TxCommand, RxData, &BytesRead), hSC30_Device);
	RxData[BytesRead] = NULL;

	std::cout << "Sent" << std::endl;
	std::cout << "The response is: ";

	for (int i = 0; i < BytesRead; i++)
	{
		std::cout << (char)RxData[i];
		if ((char)RxData[i] == '\r')
			std::cout << std::endl;
	}

	std::cout << std::endl;
	std::cout << "Done" << std::endl;

	return Rtn_OK;
}