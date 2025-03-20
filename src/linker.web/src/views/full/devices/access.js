import { getAccesss, refreshAccess } from "@/apis/access";
import { inject, provide, ref } from "vue";

const accessSymbol = Symbol();
export const provideAccess = () => {
    const access = ref({
        list: {},
        timer: 0,
        hashcode: 0
    });
    provide(accessSymbol, access);

    const handleAccesssRefresh = () => {
        refreshAccess();
    }
    const _getAccessInfo = () => {
        clearTimeout(access.value.timer);
        getAccesss(access.value.hashcode.toString()).then((res) => {
            access.value.hashcode = res.HashCode;
            if (res.List) {
                access.value.list = res.List;
            }
            access.value.timer = setTimeout(_getAccessInfo, 1000);
        }).catch(() => {
            access.value.timer = setTimeout(_getAccessInfo, 1000);
        });
    }
    const clearAccessTimeout = () => {
        clearTimeout(access.value.timer);
    }

    return {
        access, _getAccessInfo, clearAccessTimeout, handleAccesssRefresh
    }
}
export const useAccess = () => {
    return inject(accessSymbol);
}