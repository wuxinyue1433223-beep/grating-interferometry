#include "TestFunctions.h"

int ArrayTest()
{
	std::cout << std::endl;
	std::cout << "Getting friendly name of the SC30..." << std::endl;

	hFriendlyName = (ArrayHandle)GetArrayHandle(hSC30_Device, "friendly_name");
	ErrChk("GetArrayHandle", hSC30_Device);
	char array_name[STRING_LENGTH_32];
	char array_unit[STRING_LENGTH_32];
	char array_data_type[STRING_LENGTH_32];
	int access_mode = 0;
	int dimensions = 0;
	int size1 = 0;
	int size2 = 0;
	int size3 = 0;
	int channels = 0;
	SDKErrChk(GetArrayProperty(hFriendlyName, array_name, array_unit, array_data_type, &access_mode, &dimensions, &size1, &size2, &size3, &channels), hFriendlyName);

	unsigned char *friendly_name = (unsigned char *)malloc(size1 * sizeof(unsigned char));
	SDKErrChk(GetArrayDataFromDevice(hSC30_Device, hFriendlyName, _TLDeviceChannel::NO_CHANNEL, friendly_name), hSC30_Device);
	std::cout << "The friendly name of the SC30 is " << friendly_name << std::endl;

	std::cout << std::endl;
	std::cout << "Change friendly name to 'SC-30'..." << std::endl;

	friendly_name[0] = 'S';
	friendly_name[1] = 'C';
	friendly_name[2] = '-';
	friendly_name[3] = '3';
	friendly_name[4] = '0';
	friendly_name[5] = NULL;

	SDKErrChk(SetArrayDataToDevice(hSC30_Device, hFriendlyName, _TLDeviceChannel::NO_CHANNEL, friendly_name), hSC30_Device);

	wait(100);

	memset(friendly_name, 0, size1 * sizeof(unsigned char));
	std::cout << "Getting new friendly name..." << std::endl;

	SDKErrChk(GetArrayDataFromDevice(hSC30_Device, hFriendlyName, _TLDeviceChannel::NO_CHANNEL, friendly_name), hSC30_Device);
	std::cout << "The new friendly name of the SC30 is " << friendly_name << std::endl;

	std::cout << std::endl;
	std::cout << "Change to original friendly name" << std::endl;
	friendly_name[0] = 'S';
	friendly_name[1] = 'C';
	friendly_name[2] = '3';
	friendly_name[3] = '0';
	friendly_name[4] = '-';
	friendly_name[5] = 'D';
	friendly_name[6] = 'e';
	friendly_name[7] = 'v';
	friendly_name[8] = 'i';
	friendly_name[9] = 'c';
	friendly_name[10] = 'e';
	friendly_name[11] = NULL;
	SDKErrChk(SetArrayDataToDevice(hSC30_Device, hFriendlyName, _TLDeviceChannel::NO_CHANNEL, friendly_name), hSC30_Device);

	wait(100);

	memset(friendly_name, 0, size1 * sizeof(unsigned char));
	std::cout << "Getting friendly name..." << std::endl;

	SDKErrChk(GetArrayDataFromDevice(hSC30_Device, hFriendlyName, _TLDeviceChannel::NO_CHANNEL, friendly_name), hSC30_Device);
	std::cout << "The new friendly name of the SC30 is " << friendly_name << std::endl;

	delete[] friendly_name;

	return Rtn_OK;
}