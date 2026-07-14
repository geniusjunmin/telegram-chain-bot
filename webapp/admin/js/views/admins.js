import { el, safeConfirm } from '../dom.js';
import { apiFetch } from '../api.js';
import { hasPermission } from '../auth.js';

export async function render(container) {
    container.textContent = '';
    const loading = el('h2', {}, '加载管理员账号中...');
    container.appendChild(loading);

    try {
        const accounts = await apiFetch('/api/admin/accounts');
        container.textContent = '';

        const header = el('div', { className: 'header-action' },
            el('h1', {}, '管理员管理')
        );
        if (hasPermission('Admin.ManageAccounts')) {
            const btnCreate = el('button', {
                onclick: () => openCreateAdminModal(container)
            }, '新建管理员');
            header.appendChild(btnCreate);
        }
        container.appendChild(header);

        const table = el('table', {},
            el('thead', {},
                el('tr', {},
                    el('th', {}, 'ID'),
                    el('th', {}, '用户名'),
                    el('th', {}, '角色'),
                    el('th', {}, '是否激活'),
                    el('th', {}, '操作')
                )
            )
        );

        const tbody = el('tbody', {});
        accounts.forEach(a => {
            const tdActions = el('td', {});

            if (hasPermission('Admin.ManageAccounts')) {
                const btnReset = el('button', {
                    onclick: () => openResetPasswordModal(a)
                }, '重置密码');
                tdActions.appendChild(btnReset);

                if (a.role !== 'RootAdmin') {
                    const btnDelete = el('button', {
                        className: 'danger',
                        onclick: async () => {
                            if (safeConfirm(`确定要删除管理员账号 "${a.username}" 吗？`, a.username)) {
                                try {
                                    await apiFetch(`/api/admin/accounts/${a.id}`, { method: 'DELETE' });
                                    render(container);
                                } catch (e) {
                                    alert('删除失败: ' + e.message);
                                }
                            }
                        }
                    }, '删除');
                    tdActions.appendChild(document.createTextNode(' '));
                    tdActions.appendChild(btnDelete);
                }
            } else {
                tdActions.textContent = '-';
            }

            tbody.appendChild(
                el('tr', {},
                    el('td', {}, a.id.toString()),
                    el('td', {}, a.username),
                    el('td', {}, a.role),
                    el('td', {}, a.isActive ? '是' : '否'),
                    tdActions
                )
            );
        });

        table.appendChild(tbody);
        container.appendChild(el('div', { className: 'table-container' }, table));

    } catch (e) {
        container.textContent = '';
        container.appendChild(el('p', { className: 'error' }, '加载管理员失败: ' + e.message));
    }
}

function openCreateAdminModal(container) {
    const inputUser = el('input', { type: 'text', placeholder: '用户名' });
    const inputPass = el('input', { type: 'password', placeholder: '密码' });
    const selectRole = el('select', {},
        el('option', { value: 'OperatorAdmin' }, 'OperatorAdmin (运营)'),
        el('option', { value: 'AuditorAdmin' }, 'AuditorAdmin (审计)')
    );

    const modal = el('div', { className: 'modal' },
        el('div', { className: 'modal-content' },
            el('h2', {}, '新建管理员账号'),
            el('div', { className: 'form-group' },
                el('label', {}, '用户名:'),
                inputUser
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '密码:'),
                inputPass
            ),
            el('div', { className: 'form-group' },
                el('label', {}, '角色:'),
                selectRole
            ),
            el('div', { className: 'modal-actions' },
                el('button', {
                    onclick: async () => {
                        const username = inputUser.value.trim();
                        const password = inputPass.value;
                        const role = selectRole.value;

                        if (!username || !password) {
                            alert('用户名和密码不能为空');
                            return;
                        }

                        try {
                            await apiFetch('/api/admin/accounts', {
                                method: 'POST',
                                body: JSON.stringify({ username, password, role })
                            });
                            document.body.removeChild(modal);
                            render(container);
                        } catch (e) {
                            alert('创建失败: ' + e.message);
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

function openResetPasswordModal(account) {
    const inputPass = el('input', { type: 'password', placeholder: '新密码' });

    const modal = el('div', { className: 'modal' },
        el('div', { className: 'modal-content' },
            el('h2', {}, `重置密码: ${account.username}`),
            el('div', { className: 'form-group' },
                el('label', {}, '新密码:'),
                inputPass
            ),
            el('div', { className: 'modal-actions' },
                el('button', {
                    onclick: async () => {
                        const password = inputPass.value;
                        if (!password) {
                            alert('新密码不能为空');
                            return;
                        }

                        try {
                            await apiFetch(`/api/admin/accounts/${account.id}/reset-password`, {
                                method: 'POST',
                                body: JSON.stringify({ password })
                            });
                            alert('密码重置成功');
                            document.body.removeChild(modal);
                        } catch (e) {
                            alert('重置密码失败: ' + e.message);
                        }
                    }
                }, '确认重置'),
                el('button', {
                    className: 'secondary',
                    onclick: () => document.body.removeChild(modal)
                }, '取消')
            )
        )
    );
    document.body.appendChild(modal);
}
