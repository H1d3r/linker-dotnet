<template>
<el-table-column prop="MachineId" label="设备" width="220">
    <template #header>
        <div class="flex">
            <span class="flex-1">设备</span>
            <span> <el-input v-trim size="small" v-model="name" clearable @input="handleRefresh" placeholder="设备/虚拟网卡/端口转发" ></el-input> </span>
            <span>
                <el-button size="small" @click="handleRefresh"><el-icon><Search /></el-icon></el-button>
            </span>
        </div>
    </template>
    <template #default="scope">
        <div>
            <p>
                <DeviceName :config="true" :item="scope.row"></DeviceName>
            </p>
            <p class="flex">
                <template v-if="scope.row.Connected">
                    <template v-if="scope.row.showip">
                        <span title="此设备的外网IP" class="ipaddress" @click="handleExternal(scope.row)"><span>😀{{ scope.row.IP }}</span></span>
                    </template>
                    <template v-else>
                        <span title="此设备的外网IP" class="ipaddress" @click="handleExternal(scope.row)"><span>😴㊙.㊙.㊙.㊙</span></span>
                    </template>
                    <span class="flex-1"></span>
                    <UpdaterBtn v-if="scope.row.showip == false" :config="true" :item="scope.row"></UpdaterBtn>
                </template>
                <template v-else>
                    <span>{{ scope.row.LastSignIn }}</span>
                </template>
            </p>
        </div>
    </template>
</el-table-column>
</template>
<script>
import { computed, ref } from 'vue';
import {Search} from '@element-plus/icons-vue'
import UpdaterBtn from '../updater/UpdaterBtn.vue';
import DeviceName from './DeviceName.vue';
import { injectGlobalData } from '@/provide';
import { ElMessage } from 'element-plus';

export default {
    emits:['refresh'],
    components:{Search,UpdaterBtn,DeviceName},
    setup(props,{emit}) {

        const globalData = injectGlobalData();
        const hasExternal = computed(()=>globalData.value.hasAccess('ExternalShow')); 
        const name = ref(sessionStorage.getItem('search-name') || '');
        
        const handleExternal = (row)=>{
            if(!hasExternal.value) {
                ElMessage.success('无权限');
                return;
            }
            row.showip=!row.showip;
        }
        const handleRefresh = ()=>{
            sessionStorage.setItem('search-name',name.value);
            emit('refresh',name.value)
        }

        return {
             handleRefresh,name,handleExternal
        }
    }
}
</script>
<style lang="stylus" scoped>

.ipaddress{
    span{vertical-align:middle}
}

.el-input{
    width:12rem;
    margin-right:.6rem
}

</style>