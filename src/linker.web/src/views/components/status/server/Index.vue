<template>
    <div class="status-server-wrap">
        <Groups :config="config"></Groups>
        <ServerVersion :config="config"></ServerVersion>
        <Flow v-if="config && hasFlow" :config="config"></Flow>
    </div>
</template>
<script>
import { computed, reactive } from 'vue';
import Groups from '../../groups/Index.vue';
import Flow from './Flow.vue';
import ServerVersion from './Version.vue';
import { injectGlobalData } from '@/provide';
export default {
    components:{Groups,Flow,ServerVersion},
    props:['config'],
    setup(props) {

        const globalData = injectGlobalData();
        const hasFlow = computed(()=>globalData.value.hasAccess('Flow')); 

        const state = reactive({
            show: false,
            loading: false
        });

        return {
         config:props.config,hasFlow,  state
        }
    }
}
</script>
<style lang="stylus" scoped>
.status-server-wrap{
    position:relative;
    padding-right:.5rem;
    a{color:#333;}
    a+a{margin-left:.6rem;}
    .el-icon{
        vertical-align:text-bottom;
    }
}

</style>