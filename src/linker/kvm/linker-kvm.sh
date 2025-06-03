#!/bin/bash

if [ ! -f /linker/kvm/supervisord.conf ]; then
    cat >> /linker/kvm/supervisord.conf << EOF

[supervisord]
logfile = /linker/kvm/supervisord.log
logfile_maxbytes = 50MB           
pidfile = /linker/kvm/supervisord.pid 
daemon = true

[unix_http_server]
file = /linker/kvm/supervisor.sock

[supervisorctl]
serverurl = unix:///linker/kvm/supervisor.sock 

[program:linker]
command=/linker/linker
directory=/linker/
autostart=true
autorestart=true
priority=12
stopasgroup=true
stdout_logfile=/linker/kvm/stdout
stdout_logfile_maxbytes = 0
redirect_stderr=true
EOF

fi


if [ ! -f /usr/share/kvmd/extras/linker/manifest.yaml ]; then
	mkdir -p /usr/share/kvmd/extras/linker
    cat >> /usr/share/kvmd/extras/linker/manifest.yaml << EOF
name: linker
description: linker network
icon: share/svg/logo-linker.png
path: linker
daemon: kvmd-linker
place: 21

EOF

fi


python3 - <<END
import json

with open("/usr/share/kvmd/web/share/i18n/i18n_zh.json", "r", encoding='utf-8') as f:
    data = json.load(f)

data["copyright"] = "��Ȩ���� &copy; 2018-2024 Maxim Devaev | �� SilentWind ���ο��� | snltty ���ΰ�װ����Linker����"
data["kvm_text2"] = "//<a href=\"https://linker-doc.snltty.com/docs/14%E3%80%81%E4%B8%BA%E7%88%B1%E5%8F%91%E7%94%B5\">��Щ��</a>�� Linker ��Ŀ������֧���乤�����ǳ���л���ǵİ�����<br>//�����Ҳ��֧�� Linker �������� <a target=\"_blank\" href=\"https://afdian.com/a/snltty\"> ������ </a>�Ͼ��</a>��<br><br>//<a href=\"https://one-kvm.mofeng.run/thanks/#_2\">��Щ��</a>�� One-KVM ��Ŀ������֧���乤�����ǳ���л���ǵİ�����<br>//�����Ҳ��֧�� One-KVM �������� <a target=\"_blank\" href=\"https://afdian.com/a/silentwind\"> ������ </a>�Ͼ��</a>��<br><br>//<a href=\"https://github.com/pikvm/pikvm?tab=readme-ov-file#special-thanks\">��Щ��</a>�� PiKVM ��Ŀ������֧���乤�����ǳ���л���ǵİ�����<br>//�����Ҳ��֧�� PiKVM �������� <a target=\"_blank\" href=\"https://www.patreon.com/pikvm\"> Patreon</a> �� <a target=\"_blank\" href=\"https://paypal.me/pikvm\"> PayPal �Ͼ��</a>��";

with open("/usr/share/kvmd/web/share/i18n/i18n_zh.json", "w", encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)

END


supervisord -c /linker/kvm/supervisord.conf &

/kvmd/init.sh