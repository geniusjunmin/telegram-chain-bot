import { el, safeConfirm } from '../dom.js';
import { apiFetch } from '../api.js';
import { hasPermission } from '../auth.js';

export async function render(container) {
    container.textContent = '';
    const loading = el('h2', {}, '加载接龙列表中...');
    container.appendChild(loading);

    try {
        const chains = await apiFetch('/api/admin/chains');
        container.textContent = '';

        const header = el('div', { className: 'header-action' },
            el('h1', {}, '接龙列表')
        );
        container.appendChild(header);

        const table = el('table', {},
            el('thead', {},
                el('tr', {},
                    el('th', {}, 'ID'),
                    el('th', {}, '标题'),
                    el('th', {}, '创建时间'),
                    el('th', {}, '状态'),
                    el('th', {}, '操作')
                )
            )
        );

        const tbody = el('tbody', {});
        chains.forEach(c => {
            const btnView = el('button', {
                onclick: () => viewMembers(container, c.id, c.title)
            }, '查看成员');
            
            const rowActions = el('td', {}, btnView);

            if (hasPermission('Admin.ManageChains')) {
                const btnDelete = el('button', {
                    className: 'danger',
                    onclick: async () => {
                        if (safeConfirm(`确定要删除接龙 "${c.title}" 吗？所有成员数据将被软删除！`, c.title)) {
                            try {
                                await apiFetch(`/api/admin/chains/${c.id}`, { method: 'DELETE' });
                                render(container);
                            } catch (e) {
                                alert('删除失败: ' + e.message);
                            }
                        }
                    }
                }, '删除');
                rowActions.appendChild(document.createTextNode(' '));
                rowActions.appendChild(btnDelete);
            }

            tbody.appendChild(
                el('tr', {},
                    el('td', {}, c.id.toString()),
                    el('td', {}, c.title),
                    el('td', {}, new Date(c.createdAt).toLocaleString()),
                    el('td', {}, c.status === 2 ? 'Active' : 'Closed'),
                    rowActions
                )
            );
        });

        table.appendChild(tbody);
        container.appendChild(el('div', { className: 'table-container' }, table));

    } catch (e) {
        container.textContent = '';
        container.appendChild(el('p', { className: 'error' }, '加载接龙失败: ' + e.message));
    }
}

async function viewMembers(container, chainId, title) {
    container.textContent = '';
    const loading = el('h2', {}, `加载 [${title}] 的成员中...`);
    container.appendChild(loading);

    try {
        const members = await apiFetch(`/api/admin/chains/${chainId}/members`);
        container.textContent = '';

        const header = el('div', { className: 'header-action' },
            el('h1', {}, `接龙成员: ${title}`),
            el('button', { className: 'secondary', onclick: () => render(container) }, '返回接龙列表')
        );
        container.appendChild(header);

        const table = el('table', {},
            el('thead', {},
                el('tr', {},
                    el('th', {}, 'ID'),
                    el('th', {}, '显示名字'),
                    el('th', {}, 'Telegram 用户名'),
                    el('th', {}, '加入时间'),
                    el('th', {}, '操作')
                )
            )
        );

        const tbody = el('tbody', {});
        members.forEach(m => {
            const tdActions = el('td', {});

            if (hasPermission('Admin.ManageChains')) {
                const btnEdit = el('button', {
                    onclick: () => openEditMemberModal(container, chainId, title, m)
                }, '修改');
                const btnDelete = el('button', {
                    className: 'danger',
                    onclick: async () => {
                        if (safeConfirm(`确定要将成员 "${m.displayName}" 从接龙中移除吗？`)) {
                            try {
                                await apiFetch(`/api/admin/members/${m.id}`, { method: 'DELETE' });
                                viewMembers(container, chainId, title);
                            } catch (e) {
                                alert('移除成员失败: ' + e.message);
                            }
                        }
                    }
                }, '移除');
                tdActions.appendChild(btnEdit);
                tdActions.appendChild(document.createTextNode(' '));
                tdActions.appendChild(btnDelete);
            } else {
                tdActions.textContent = '-';
            }

            tbody.appendChild(
                el('tr', {},
                    el('td', {}, m.id.toString()),
                    el('td', {}, m.displayName),
                    el('td', {}, m.telegramUsername || '-'),
                    el('td', {}, new Date(m.joinedAt).toLocaleString()),
                    tdActions
                )
            );
        });

        table.appendChild(tbody);
        container.appendChild(el('div', { className: 'table-container' }, table));

    } catch (e) {
        container.textContent = '';
        container.appendChild(el('p', { className: 'error' }, '加载成员失败: ' + e.message));
    }
}

function openEditMemberModal(container, chainId, title, member) {
    const inputUsername = el('input', { type: 'text', value: member.displayName });
    const inputNickname = el('input', { type: 'text', value: member.telegramUsername || '' });
    
    const modal = el('div', { className: 'modal' },
        el('div', { className: 'modal-content' },
            el('h2', {}, '修改成员'),
            el('div', { className: 'form-group' },
                el('label', {}, '显示名称:'),
                inputUsername
            ),
            el('div', { className: 'form-group' },
                el('label', {}, 'Telegram 用户名:'),
                inputNickname
            ),
            el('div', { className: 'modal-actions' },
                el('button', {
                    onclick: async () => {
                        const displayName = inputUsername.value.trim();
                        const telegramUsername = inputNickname.value.trim();
                        if (!displayName) {
                            alert('显示名称不能为空');
                            return;
                        }
                        try {
                            await apiFetch(`/api/admin/members/${member.id}`, {
                                method: 'PUT',
                                body: JSON.stringify({ displayName, telegramUsername })
                            });
                            document.body.removeChild(modal);
                            viewMembers(container, chainId, title);
                        } catch (e) {
                            alert('更新失败: ' + e.message);
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
