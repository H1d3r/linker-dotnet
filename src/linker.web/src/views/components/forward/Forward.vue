<template>
    <el-table-column prop="forward" :label="forward.show?'转发/穿透':''" >
        <template #default="scope">
            <template v-if="forward.show && scope.row.Connected">
                <template v-if="scope.row.isSelf && (hasForwardShowSelf || hasForwardSelf)">
                    <div class="nowrap">
                        <ConnectionShow :data="connections.list[scope.row.MachineId]" :row="scope.row" transitionId="forward"></ConnectionShow>
                        <a href="javascript:;" title="管理自己的端口转发" :class="{green:forward.list[scope.row.MachineId]>0 }" @click="handleEdit(scope.row.MachineId,scope.row.MachineName)">
                            <span :class="{gateway:forward.list[scope.row.MachineId]>0}">端口转发({{forward.list[scope.row.MachineId]>99 ? '99+' : forward.list[scope.row.MachineId]}})</span>
                        </a>
                    </div>
                    <div class="nowrap">
                        <a href="javascript:;" title="管理自己的内网穿透" :class="{green:sforward.list[scope.row.MachineId]>0}" @click="handleSEdit(scope.row.MachineId,scope.row.MachineName)">
                            <span :class="{gateway:sforward.list[scope.row.MachineId]>0 }">内网穿透({{sforward.list[scope.row.MachineId]>99 ? '99+' : sforward.list[scope.row.MachineId]}})</span>
                        </a>
                    </div>
                </template>
                <template v-else-if="hasForwardShowOther || hasForwardOther">
                    <div class="nowrap">
                        <ConnectionShow :data="connections.list[scope.row.MachineId]" :row="scope.row" transitionId="forward"></ConnectionShow>
                        <a href="javascript:;" title="管理自己的端口转发" :class="{green:forward.list[scope.row.MachineId]>0}" @click="handleEdit(scope.row.MachineId,scope.row.MachineName)">
                            <span :class="{gateway:forward.list[scope.row.MachineId]>0}">端口转发({{forward.list[scope.row.MachineId]>99 ? '99+' : forward.list[scope.row.MachineId]}})</span>
                        </a>
                    </div>
                    <div class="nowrap">
                        <a href="javascript:;" title="管理自己的内网穿透" :class="{green:sforward.list[scope.row.MachineId]>0}" @click="handleSEdit(scope.row.MachineId,scope.row.MachineName)">
                            <span :class="{gateway:sforward.list[scope.row.MachineId]>0 }">内网穿透({{sforward.list[scope.row.MachineId]>99 ? '99+' : sforward.list[scope.row.MachineId]}})</span>
                        </a>
                    </div>
                </template>
            </template>
        </template>
    </el-table-column>
</template>
<script>
import { injectGlobalData } from '@/provide';
import { useForward } from './forward';
import { useSforward } from './sforward';
import { computed } from 'vue';
import { useForwardConnections } from '../connection/connections';
import ConnectionShow from '../connection/ConnectionShow.vue';
import { ElMessage } from 'element-plus';

export default {
    emits: ['edit','sedit'],
    components:{ConnectionShow},
    setup(props, { emit }) {

        const forward = useForward()
        const sforward = useSforward()
        const globalData = injectGlobalData();
        const machineId = computed(() => globalData.value.config.Client.Id);
        const hasForwardShowSelf = computed(()=>globalData.value.hasAccess('ForwardShowSelf')); 
        const hasForwardShowOther = computed(()=>globalData.value.hasAccess('ForwardShowOther')); 
        const hasForwardSelf = computed(()=>globalData.value.hasAccess('ForwardSelf')); 
        const hasForwardOther = computed(()=>globalData.value.hasAccess('ForwardOther')); 
        const connections = useForwardConnections();

        const handleEdit = (_machineId,_machineName)=>{
            if(machineId.value === _machineId){
                if(!hasForwardSelf.value){
                    ElMessage.success('无权限');
                    return;
                }
            }else{
                if(!hasForwardOther.value){
                    ElMessage.success('无权限');
                    return;
                }
            }
            forward.value.machineId = _machineId;
            forward.value.machineName = _machineName;
            forward.value.showEdit = true;
        }
        const handleSEdit = (_machineId,_machineName)=>{
            if(machineId.value === _machineId){
                if(!hasForwardSelf.value){
                ElMessage.success('无权限');
                return;
            }
            }else{
                if(!hasForwardOther.value){
                ElMessage.success('无权限');
                return;
            }
            }
            sforward.value.machineid = _machineId;
            sforward.value.machineName = _machineName;
            sforward.value.showEdit = true;
        }
        const handleForwardRefresh = ()=>{
            emit('refresh');
        }

        return {
            forward,sforward,hasForwardShowSelf,hasForwardShowOther,connections, handleEdit,handleSEdit,handleForwardRefresh
        }
    }
}
</script>
<style lang="stylus" scoped>
a{
    text-decoration: underline;
    &+a{margin-left:1rem}
    &.green{
        font-weight:bold;
    }
}
</style>