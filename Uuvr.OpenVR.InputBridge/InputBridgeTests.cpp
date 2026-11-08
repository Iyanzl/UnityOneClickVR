#include "openvr.h"
#include <iostream>
#include <string>
#include <cstring>
#include <stdexcept>
using namespace vr;
namespace {
int checks = 0;
void Check(bool condition, const char* name) { if (!condition) throw std::runtime_error(name); ++checks; }
struct FakeInput {
    int count=0, updates=0, origin=-1;
    bool active=true;
    EVRInputError manifestError=VRInputError_None, updateError=VRInputError_None;
    EVRInputError SetActionManifestPath(const char*) { return manifestError; }
    EVRInputError GetActionHandle(const char*, VRActionHandle_t* handle) { *handle = ++count; return VRInputError_None; }
    EVRInputError GetActionSetHandle(const char*, VRActionSetHandle_t* handle) { *handle=99; return VRInputError_None; }
    EVRInputError UpdateActionState(VRActiveActionSet_t* sets, uint32_t size, uint32_t n) {
        Check(size==sizeof(VRActiveActionSet_t) && n==1 && sets[0].ulActionSet==99, "action set ABI");
        ++updates; return updateError;
    }
    EVRInputError GetPoseActionDataRelativeToNow(VRActionHandle_t handle, ETrackingUniverseOrigin o, float seconds,
        InputPoseActionData_t* data, uint32_t size, VRInputValueHandle_t source) {
        Check(size==sizeof(*data) && seconds==0 && source==0, "pose ABI"); origin=o;
        data->bActive=active; data->pose.bDeviceIsConnected=true; data->pose.bPoseIsValid=true;
        data->pose.mDeviceToAbsoluteTracking.m[0][3]=static_cast<float>(handle);
        return VRInputError_None;
    }
    EVRInputError GetDigitalActionData(VRActionHandle_t handle, InputDigitalActionData_t* data, uint32_t size, VRInputValueHandle_t source) {
        Check(size==sizeof(*data) && source==0, "digital ABI");
        data->bActive=active; data->bState=handle==3 || handle==12 || handle==13; return VRInputError_None;
    }
    EVRInputError GetAnalogActionData(VRActionHandle_t handle, InputAnalogActionData_t* data, uint32_t size, VRInputValueHandle_t source) {
        Check(size==sizeof(*data) && source==0, "analog ABI");
        data->bActive=active; data->x=static_cast<float>(handle)/20; data->y=-.5f; return VRInputError_None;
    }
} fake;
bool available=true;
FakeInput* TestInput() { return available ? &fake : nullptr; }
struct FakeApplications {
    EVRApplicationError addError=VRApplicationError_None, identifyError=VRApplicationError_None;
    bool temporary=false, mismatch=false;
    int adds=0, identifies=0;
    std::string key;
    EVRApplicationError AddApplicationManifest(const char*, bool temp) { ++adds; temporary=temp; return addError; }
    EVRApplicationError IdentifyApplication(uint32_t pid, const char* k) {
        Check(pid==42, "only caller process identified"); ++identifies; key=k; return identifyError;
    }
    EVRApplicationError GetApplicationKeyByProcessId(uint32_t pid, char* out, uint32_t size) {
        Check(pid==42 && size==k_unMaxApplicationKeyLength, "application key ABI");
        strcpy_s(out,size,mismatch ? "system.generated.old.exe" : key.c_str()); return VRApplicationError_None;
    }
} apps;
FakeApplications* TestApplications() { return available ? &apps : nullptr; }
}
#define HYBRID_INPUT_TEST
#include "InputBridge.cpp"
int main() {
    try {
        Check(HybridInput_Abi()==2 && sizeof(InputFrame)==256, "bridge ABI");
        Check(offsetof(HandInput,gripPose)==24 && offsetof(HandInput,aimPose)==72 && offsetof(HandInput,active)==124, "hand offsets");
        InputFrame f{}; f.left.buttons=123;
        Check(HybridInput_Read(&f,256,1)==VRInputError_InvalidHandle && f.left.buttons==0, "read before init clears state");
        Check(HybridInput_Read(nullptr,256,1)==VRInputError_InvalidParam, "null destination");
        Check(HybridInput_Read(&f,255,1)==VRInputError_InvalidParam, "wrong buffer size");
        Check(HybridInput_Initialize(nullptr)==VRInputError_InvalidParam, "null path");
        Check(HybridInput_Initialize("actions.json")==VRInputError_InvalidHandle, "unidentified application cannot inherit legacy input");
        Check(HybridInput_IdentifyApplication(nullptr,"test",42)==VRApplicationError_InvalidParameter, "missing app manifest rejected");
        Check(HybridInput_IdentifyApplication("app.json","",42)==VRApplicationError_InvalidParameter, "empty app key rejected");
        Check(HybridInput_IdentifyApplication("app.json","test",0)==VRApplicationError_InvalidParameter, "zero pid rejected");
        available=false; Check(HybridInput_IdentifyApplication("app.json","test",42)==VRApplicationError_IPCFailed, "missing runtime"); available=true;
        apps.addError=VRApplicationError_InvalidManifest;
        Check(HybridInput_IdentifyApplication("app.json","test",42)==VRApplicationError_InvalidManifest && apps.identifies==0, "registration failure stops identification");
        apps.addError=VRApplicationError_None; apps.identifyError=VRApplicationError_UnknownApplication;
        Check(HybridInput_IdentifyApplication("app.json","test",42)==VRApplicationError_UnknownApplication, "identify failure propagated");
        apps.identifyError=VRApplicationError_None; apps.mismatch=true;
        Check(HybridInput_IdentifyApplication("app.json","test",42)==VRApplicationError_InvalidApplication, "stale generated key rejected");
        apps.mismatch=false;
        Check(HybridInput_IdentifyApplication("app.json","test",42)==0 && apps.temporary, "temporary isolated app registration verified");
        available=false; Check(HybridInput_Initialize("actions.json")==VRInputError_IPCError, "input interface missing"); available=true;
        fake.manifestError=VRInputError_InvalidParam;
        Check(HybridInput_Initialize("actions.json")==VRInputError_InvalidParam && fake.count==0, "manifest failure stops init");
        fake.manifestError=VRInputError_None;
        Check(HybridInput_Initialize("actions.json")==0 && fake.count==16, "all hand actions loaded");
        Check(HybridInput_Read(&f,256,1)==0 && fake.updates==1 && fake.origin==1, "one update per frame and same origin");
        Check(f.left.buttons==1 && f.right.buttons==6, "hands and digital bits independent");
        Check(f.left.trigger==.3f && f.left.grip==.35f && f.right.trigger==.7f && f.right.grip==.75f, "trigger and grip offsets");
        Check(f.left.x==.4f && f.right.x==.8f && f.right.y==-.5f, "stick axes independent");
        Check(f.left.gripPose.m[0][3]==1 && f.right.gripPose.m[0][3]==9 && f.right.aimPose.m[0][3]==10, "grip and aim poses independent");
        Check(f.left.valid && f.right.aimValid && f.left.active==255, "active pose flags");
        fake.active=false;
        Check(HybridInput_Read(&f,256,0)==0 && !f.left.valid && !f.right.aimValid && !f.left.buttons && !f.right.trigger && !f.right.active, "inactive data never holds inputs");
        fake.updateError=VRInputError_NoData; f.right.buttons=123;
        Check(HybridInput_Read(&f,256,1)==VRInputError_NoData && f.right.buttons==0, "update failure clears state");
        Check(HybridInput_Read(&f,256,3)==VRInputError_InvalidParam, "invalid origin");
        HybridInput_Reset();
        Check(HybridInput_Read(&f,256,1)==VRInputError_InvalidHandle, "reset needs reinit");
        Check(HybridInput_Initialize("actions.json")==VRInputError_InvalidHandle, "new session needs identity again");
        std::cout << checks << " input bridge contract checks passed; no VR runtime initialized\n";
        return 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
