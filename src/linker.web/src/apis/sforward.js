import { sendWebsocketMsg } from './request'

export const getSForwardInfo = (data) => {
    return sendWebsocketMsg('sforward/get', data);
}
export const refreshSForward = () => {
    return sendWebsocketMsg('sforward/refresh');
}
export const getSForwardCountInfo = (hashcode = '0') => {
    return sendWebsocketMsg('sforward/getcount', hashcode);
}
export const removeSForwardInfo = (data) => {
    return sendWebsocketMsg('sforward/remove', data);
}
export const addSForwardInfo = (data) => {
    return sendWebsocketMsg('sforward/add', data);
}

export const testLocalSForwardInfo = (data) => {
    return sendWebsocketMsg('sforward/TestLocal', data);
}

export const startSForwardInfo = (data) => {
    return sendWebsocketMsg('sforward/start', data);
}
export const stopSForwardInfo = (data) => {
    return sendWebsocketMsg('sforward/stop', data);
}