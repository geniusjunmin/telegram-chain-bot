import { el } from '../dom.js';
import { apiFetch } from '../api.js';
import { hasPermission } from '../auth.js';

export async function render(container) {
    container.textContent = '';
    const loading = el('h2', {}, '加载系统设置中...');
    container.appendChild(loading);

    try {
        const settings = await apiFetch('/api/admin/system-settings');
        container.textContent = '';

        const header = el('h1', {}, '全局系统设置');
        container.appendChild(header);

        const selectWhitelistMode = el('select', { disabled: !hasPermission('Admin.ManageSettings') },
            el('option', { value: '1', selected: settings.whitelistMode === 1 || settings.whitelistMode === 'Audit' }, 'Audit (审计模式，记录新群聊为Pending)'),
            el('option', { value: '2', selected: settings.whitelistMode === 2 || settings.whitelistMode === 'Enforced' }, 'Enforced (强制模式，仅批准群可创建)')
        );

        const selectUnauthorizedChatBehavior = el('select', { disabled: !hasPermission('Admin.ManageSettings') },
            el('option', { value: 'WarnAndLeave', selected: settings.unauthorizedChatBehavior === 'WarnAndLeave' }, 'WarnAndLeave (警告并退出)'),
            el('option', { value: 'SilentLeave', selected: settings.unauthorizedChatBehavior === 'SilentLeave' }, 'SilentLeave (静默退出)'),
            el('option', { value: 'Ignore', selected: settings.unauthorizedChatBehavior === 'Ignore' }, 'Ignore (忽略)')
        );

        const inputDefaultCreatePolicy = el('select', { disabled: !hasPermission('Admin.ManageSettings') },
            el('option', { value: '0', selected: settings.defaultCreatePolicy === 0 || settings.defaultCreatePolicy === 'Everyone' }, '所有成员均可创建'),
            el('option', { value: '1', selected: settings.defaultCreatePolicy === 1 || settings.defaultCreatePolicy === 'ChatAdministrators' }, '仅群管理员可创建'),
            el('option', { value: '2', selected: settings.defaultCreatePolicy === 2 || settings.defaultCreatePolicy === 'BotOwners' }, '仅机器人拥有者可创建')
        );

        const inputDefaultMaxMembers = el('input', { type: 'number', value: settings.defaultMaxMembers, disabled: !hasPermission('Admin.ManageSettings') });
        const inputDefaultExpiryHours = el('input', { type: 'number', value: settings.defaultChainExpiryHours, disabled: !hasPermission('Admin.ManageSettings') });
        const inputMaxActiveChains = el('input', { type: 'number', value: settings.maxActiveChainsPerChat, disabled: !hasPermission('Admin.ManageSettings') });
        const inputInitDataMaxAge = el('input', { type: 'number', value: settings.telegramInitDataMaxAgeSeconds, disabled: !hasPermission('Admin.ManageSettings') });
        const inputDataRetention = el('input', { type: 'number', value: settings.deletedDataRetentionDays, disabled: !hasPermission('Admin.ManageSettings') });
        const inputBotToken = el('input', { type: 'password', value: settings.botToken || '', placeholder: '请输入 Telegram Bot Token', disabled: !hasPermission('Admin.ManageSettings') });
 
        const card = el('div', { className: 'card change-pw-container' },
            el('div', { className: 'form-group' },
                el('label', {}, '白名单模式:'),
                selectWhitelistMode
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '未授权群聊处理方式:'),
                selectUnauthorizedChatBehavior
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '新群默认接龙创建策略:'),
                inputDefaultCreatePolicy
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '默认最大接龙成员数:'),
                inputDefaultMaxMembers
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '默认接龙失效时长 (小时):'),
                inputDefaultExpiryHours
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '单群默认最大活动接龙数:'),
                inputMaxActiveChains
            ),
            el('div', { className: 'form-group' },
                el('label', {}, 'Telegram 凭据有效期 (秒):'),
                inputInitDataMaxAge
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '被删除数据保留时长 (天):'),
                inputDataRetention
            ),
            el('div', { className: 'form-group' },
                el('label', {}, 'Telegram Bot Token (BOT_TOKEN):'),
                inputBotToken
            )
        );
 
        if (hasPermission('Admin.ManageSettings')) {
            const btnSave = el('button', {
                onclick: async () => {
                    const payload = {
                        id: 1,
                        whitelistMode: parseInt(selectWhitelistMode.value),
                        unauthorizedChatBehavior: selectUnauthorizedChatBehavior.value,
                        defaultCreatePolicy: parseInt(inputDefaultCreatePolicy.value),
                        defaultMaxMembers: parseInt(inputDefaultMaxMembers.value),
                        defaultChainExpiryHours: parseInt(inputDefaultExpiryHours.value),
                        maxActiveChainsPerChat: parseInt(inputMaxActiveChains.value),
                        telegramInitDataMaxAgeSeconds: parseInt(inputInitDataMaxAge.value),
                        deletedDataRetentionDays: parseInt(inputDataRetention.value),
                        requireMfaForSuperAdmin: settings.requireMfaForSuperAdmin,
                        botToken: inputBotToken.value
                    };

                    if (Object.values(payload).some(v => typeof v === 'number' && isNaN(v))) {
                        alert('请输入有效的数字值');
                        return;
                    }

                    try {
                        await apiFetch('/api/admin/system-settings', {
                            method: 'POST',
                            body: JSON.stringify(payload)
                        });
                        alert('保存设置成功');
                        render(container);
                    } catch (e) {
                        alert('保存失败: ' + e.message);
                    }
                }
            }, '保存设置');
            card.appendChild(btnSave);
        }

        container.appendChild(card);

    } catch (e) {
        container.textContent = '';
        container.appendChild(el('p', { className: 'error' }, '加载系统设置失败: ' + e.message));
    }
}
