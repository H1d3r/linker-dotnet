import { getSignInList, signInDel, setSignInOrder } from "@/apis/signin";
import { injectGlobalData } from "@/provide";
import { computed, inject, provide, reactive, ref } from "vue";

const queue = [];

const deviceSymbol = Symbol();
export const provideDevices = () => {
    //https://api.ipbase.com/v1/json/8.8.8.8
    const globalData = injectGlobalData();
    const machineId = computed(() => globalData.value.config.Client.Id);
    const devices = reactive({
        timer: 0,
        page: {
            Request: {
                Page: 1, Size: +(localStorage.getItem('ps') || '10'), Name: '', Ids: [], Prop: '', Asc: true
            },
            Count: 0,
            List: []
        },

        showDeviceEdit: false,
        showAccessEdit: false,
        deviceInfo: null
    });
    provide(deviceSymbol, devices);
    const _getSignList = () => {
        getSignInList(devices.page.Request).then((res) => {
            devices.page.Request = res.Request;
            devices.page.Count = res.Count;
            for (let j in res.List) {
                Object.assign(res.List[j], {
                    showDel: machineId.value != res.List[j].MachineId && res.List[j].Connected == false,
                    showAccess: machineId.value != res.List[j].MachineId && res.List[j].Connected,
                    showReboot: res.List[j].Connected,
                    isSelf: machineId.value == res.List[j].MachineId,
                    showip: false
                });
                if (res.List[j].isSelf) {
                    globalData.value.self = res.List[j];
                }
            }
            devices.page.List = res.List;
            for (let i = 0; i < devices.page.List.length; i++) {
                queue.push(devices.page.List[i]);
            }
        }).catch((err) => { });
    }
    const _getSignList1 = () => {
        clearTimeout(devices.timer);
        getSignInList(devices.page.Request).then((res) => {
            for (let j in res.List) {
                const item = devices.page.List.filter(c => c.MachineId == res.List[j].MachineId)[0];
                if (item) {
                    Object.assign(item, {
                        Connected: res.List[j].Connected,
                        Version: res.List[j].Version,
                        LastSignIn: res.List[j].LastSignIn,
                        Args: res.List[j].Args,
                        showDel: machineId.value != res.List[j].MachineId && res.List[j].Connected == false,
                        showAccess: machineId.value != res.List[j].MachineId && res.List[j].Connected,
                        showReboot: res.List[j].Connected,
                        isSelf: machineId.value == res.List[j].MachineId,
                    });
                    if (item.isSelf) {
                        globalData.value.self = item;
                    }
                }
            }
            devices.timer = setTimeout(_getSignList1, 5000);
        }).catch((err) => {
            devices.timer = setTimeout(_getSignList1, 5000);
        });
    }
    const handleDeviceEdit = (row) => {
        devices.deviceInfo = row;
        devices.showDeviceEdit = true;
    }
    const handleAccessEdit = (row) => {
        devices.deviceInfo = row;
        devices.showAccessEdit = true;
    }
    const handlePageChange = (page) => {
        if (page) {
            devices.page.Request.Page = page;
        }
        _getSignList();
    }
    const handlePageSizeChange = (size) => {
        if (size) {
            devices.page.Request.Size = size;
            localStorage.setItem('ps', size);
        }
        _getSignList();
    }
    const handleDel = (name) => {
        signInDel(name).then(() => {
            _getSignList();
        });
    }
    const clearDevicesTimeout = () => {
        clearTimeout(devices.timer);
        devices.timer = 0;
    }

    const setSort = (ids) => {
        return setSignInOrder(ids);
    }

    return {
        devices, machineId, _getSignList, _getSignList1, handleDeviceEdit, handleAccessEdit, handlePageChange, handlePageSizeChange, handleDel, clearDevicesTimeout,
        setSort
    }
}
export const useDevice = () => {
    return inject(deviceSymbol);
}