#include "TestFunctions.h"

int ProfileTest()
{
	std::cout << std::endl;
	std::cout << "This example is to demonstrate how to load/save profile from/to the SC30." << std::endl;
	std::cout << std::endl;

	std::cout << "Getting the name of profile 1..." << std::endl;
	
	ArrayHandle hProfileName = (ArrayHandle)GetArrayHandle(hSC30_Device, "profile_name");
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
	SDKErrChk(GetArrayProperty(hProfileName, array_name, array_unit, array_data_type, &access_mode, &dimensions, &size1, &size2, &size3, &channels), hFriendlyName);

	// Get profile name of channel 1
	unsigned char *profile_name = (unsigned char *)malloc(size1 * sizeof(unsigned char));
	SDKErrChk(GetArrayDataFromDevice(hSC30_Device, hProfileName, _TLDeviceChannel::CHANNEL1, profile_name), hSC30_Device);
	std::cout << "The profile 1 name is " << profile_name << std::endl;

	wait(500);

	// Get profile name of channel 4
	SDKErrChk(GetArrayDataFromDevice(hSC30_Device, hProfileName, _TLDeviceChannel::CHANNEL4, profile_name), hSC30_Device);
	std::cout << "The profile 4 name is " << profile_name << std::endl;

	wait(500);

	// Change profile1 name to "TestSlot1"
	std::cout << "Change Profile 1 name, press a key to continue..." << std::endl;
	unsigned char *new_profile_name = (unsigned char *)malloc(size1 * sizeof(unsigned char));
	new_profile_name[0] = 'T';
	new_profile_name[1] = 'e';
	new_profile_name[2] = 's';
	new_profile_name[3] = 't';
	new_profile_name[4] = 'S';
	new_profile_name[5] = 'l';
	new_profile_name[6] = 'o';
	new_profile_name[7] = 't';
	new_profile_name[8] = '1';
	new_profile_name[9] = NULL;
	SDKErrChk(SetArrayDataToDevice(hSC30_Device, hProfileName, _TLDeviceChannel::CHANNEL1, new_profile_name), hSC30_Device);

	wait(500);
	getchar();

	SDKErrChk(GetArrayDataFromDevice(hSC30_Device, hProfileName, _TLDeviceChannel::CHANNEL1, profile_name), hSC30_Device);
	std::cout << "The new profile 1 name is " << profile_name << std::endl;

	wait(500);

	// Save Startup profile
	std::cout << "Save to Startup profile, press a key to continue..." << std::endl;
	ParameterHandle hStartupProfileOp = GetParameterHandle(hSC30_Device, "startup_profile_op");
	unsigned char startup_profile_op = 1;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hStartupProfileOp, _TLDeviceChannel::NO_CHANNEL, &startup_profile_op, sizeof(unsigned char)), hSC30_Device);
	wait(500);
	getchar();

	// Restore Factory profile
	std::cout << "Restore to the Factory profile, press a key to continue..." << std::endl;
	ParameterHandle hRestoreFactoryProfile = GetParameterHandle(hSC30_Device, "restore_factory_profile");
	unsigned char restore_factory_profile = 0;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hRestoreFactoryProfile, _TLDeviceChannel::NO_CHANNEL, &restore_factory_profile, sizeof(unsigned char)), hSC30_Device);	
	wait(500);
	getchar();

	// Load Startup profile
	std::cout << "Load Startup profile, press a key to continue..." << std::endl;
	startup_profile_op = 0;
	SDKErrChk(SetParameterValueToDevice(hSC30_Device, hStartupProfileOp, _TLDeviceChannel::NO_CHANNEL, &startup_profile_op, sizeof(unsigned char)), hSC30_Device);
	wait(500);
	getchar();

	delete[] profile_name;
	delete[] new_profile_name;

	return Rtn_OK;
}