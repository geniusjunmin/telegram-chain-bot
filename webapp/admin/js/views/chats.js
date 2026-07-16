import { el, safeConfirm } from '../dom.js';
import { apiFetch } from '../api.js';
import { hasPermission } from '../auth.js';

export async function render(container) {
    container.textContent = '';
    const loading = el('h2', {}, '加载群聊列表中...');
    container.appendChild(loading);

    try {
        const res = await apiFetch('/api/admin/chats?page=1&pageSize=100');
        container.textContent = '';

        const chats = res.items || [];

        const header = el('div', { className: 'header-action' },
            el('h1', {}, '群聊管理')
        );
        container.appendChild(header);

        const table = el('table', {},
            el('thead', {},
                el('tr', {},
                    el('th', {}, '群组 ID'),
                    el('th', {}, '标题'),
                    el('th', {}, '状态'),
                    el('th', {}, '创建策略'),
                    el('th', {}, '操作')
                )
            )
        );

        const tbody = el('tbody', {});
        chats.forEach(chat => {
            const tdActions = el('td', {});

            if (hasPermission('Admin.ManageChains')) {
                // If pending, show Approve / Block
                if (chat.authorizationStatus === 0 || chat.authorizationStatus === 'Pending') {
                    const btnApprove = el('button', {
                        onclick: async () => {
                            try {
                                await apiFetch(`/api/admin/chats/${chat.chatId}/approve`, { method: 'POST' });
                                render(container);
                            } catch (e) {
                                alert('审批批准失败: ' + e.message);
                            }
                        }
                    }, '批准');
                    const btnBlock = el('button', {
                        className: 'danger',
                        onclick: async () => {
                            try {
                                await apiFetch(`/api/admin/chats/${chat.chatId}/block`, { method: 'POST' });
                                render(container);
                            } catch (e) {
                                alert('审批拒绝失败: ' + e.message);
                            }
                        }
                    }, '拒绝');
                    tdActions.appendChild(btnApprove);
                    tdActions.appendChild(document.createTextNode(' '));
                    tdActions.appendChild(btnBlock);
                } else {
                    // Approved or Blocked
                    const btnEdit = el('button', {
                        onclick: () => openEditChatModal(container, chat)
                    }, '修改配置');
                    tdActions.appendChild(btnEdit);

                    if (chat.authorizationStatus !== 2 && chat.authorizationStatus !== 'Blocked') {
                        const btnBlock = el('button', {
                            className: 'danger',
                            onclick: async () => {
                                if (safeConfirm(`确定要封禁群组 "${chat.title}" 吗？`)) {
                                    try {
                                        await apiFetch(`/api/admin/chats/${chat.chatId}/block`, { method: 'POST' });
                                        render(container);
                                    } catch (e) {
                                        alert('封禁失败: ' + e.message);
                                    }
                                }
                            }
                        }, '封禁');
                        tdActions.appendChild(document.createTextNode(' '));
                        tdActions.appendChild(btnBlock);
                    } else {
                        const btnApprove = el('button', {
                            onclick: async () => {
                                try {
                                    await apiFetch(`/api/admin/chats/${chat.chatId}/approve`, { method: 'POST' });
                                    render(container);
                                } catch (e) {
                                    alert('解封失败: ' + e.message);
                                }
                            }
                        }, '解封');
                        tdActions.appendChild(document.createTextNode(' '));
                        tdActions.appendChild(btnApprove);
                    }
                }
            } else {
                tdActions.textContent = '-';
            }

            tbody.appendChild(
                el('tr', {},
                    el('td', {}, chat.chatId.toString()),
                    el('td', {}, chat.title),
                    el('td', {}, chat.authorizationStatus.toString()),
                    el('td', {}, getCreatePolicyText(chat.createPolicy)),
                    tdActions
                )
            );
        });

        table.appendChild(tbody);
        container.appendChild(el('div', { className: 'table-container' }, table));

    } catch (e) {
        container.textContent = '';
        container.appendChild(el('p', { className: 'error' }, '加载群聊失败: ' + e.message));
    }
}
function getCreatePolicyText(policy) {
    const p = parseInt(policy);
    switch (p) {
        case 1: return '所有成员';
        case 2: return '仅管理员';
        case 3: return '仅机器人拥有者';
        case 4: return '完全禁止';
        default: return policy?.toString() || '未知';
    }
}

function openEditChatModal(container, chat) {
    const inputJoinEnabled = el('select', {},
        el('option', { value: 'true', selected: chat.isJoinEnabled === true }, '允许加入'),
        el('option', { value: 'false', selected: chat.isJoinEnabled === false }, '禁止加入')
    );
    const inputMaxMembers = el('input', { type: 'number', value: chat.defaultMaxMembers });
    const inputMaxActiveChains = el('input', { type: 'number', value: chat.maxActiveChains });
    
    const inputCreatePolicy = el('select', {},
        el('option', { value: '1', selected: chat.createPolicy === 1 || chat.createPolicy === 'Everyone' }, '所有成员均可创建'),
        el('option', { value: '2', selected: chat.createPolicy === 2 || chat.createPolicy === 'ChatAdministrators' }, '仅群管理员可创建'),
        el('option', { value: '3', selected: chat.createPolicy === 3 || chat.createPolicy === 'BotOwners' }, '仅机器人拥有者可创建'),
        el('option', { value: '4', selected: chat.createPolicy === 4 || chat.createPolicy === 'Disabled' }, '完全禁止创建')
    );

    const modal = el('div', { className: 'modal' },
        el('div', { className: 'modal-content' },
            el('h2', {}, '修改群聊配置'),
            el('div', { className: 'form-group' },
                el('label', {}, '允许加入:'),
                inputJoinEnabled
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '默认最大成员数:'),
                inputMaxMembers
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '最大活动接龙数量:'),
                inputMaxActiveChains
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '接龙创建策略:'),
                inputCreatePolicy
            ),
            el('div', { className: 'modal-actions' },
                el('button', {
                    onclick: async () => {
                        const isJoinEnabled = inputJoinEnabled.value === 'true';
                        const defaultMaxMembers = parseInt(inputMaxMembers.value);
                        const maxActiveChains = parseInt(inputMaxActiveChains.value);
                        const createPolicy = parseInt(inputCreatePolicy.value);

                        if (isNaN(defaultMaxMembers) || isNaN(maxActiveChains)) {
                            alert('请输入有效的数字');
                            return;
                        }

                        try {
                            await apiFetch(`/api/admin/chats/${chat.chatId}`, {
                                method: 'PUT',
                                body: JSON.stringify({ isJoinEnabled, defaultMaxMembers, maxActiveChains, createPolicy })
                            });
                            document.body.removeChild(modal);
                            render(container);
                        } catch (e) {
                            alert('保存配置失败: ' + e.message);
                        }
                    }
                }, '保存'),
                el('button', {
                    className: 'secondary',
                    onclick: () => document.body.removeChild(modal)
                }, '取消')
            )
        )
    );
    document.body.appendChild(modal);
}
