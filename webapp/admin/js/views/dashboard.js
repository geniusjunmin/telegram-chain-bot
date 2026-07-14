import { el } from '../dom.js';
import { apiFetch } from '../api.js';

export async function render(container) {
    container.textContent = '';
    
    const loading = el('h2', {}, '加载仪表盘中...');
    container.appendChild(loading);

    try {
        const stats = await apiFetch('/api/admin/dashboard-stats');
        const chats = await apiFetch('/api/admin/chats');
        
        container.textContent = '';

        // Statistics grid
        const statsGrid = el('div', { className: 'stats-grid' },
            el('div', { className: 'stat-card' },
                el('div', { className: 'stat-label' }, '活动接龙'),
                el('div', { className: 'stat-value' }, stats.total_active_chains)
            ),
            el('div', { className: 'stat-card' },
                el('div', { className: 'stat-label' }, '总群组'),
                el('div', { className: 'stat-value' }, stats.total_groups)
            ),
            el('div', { className: 'stat-card' },
                el('div', { className: 'stat-label' }, '今日加入人次'),
                el('div', { className: 'stat-value' }, stats.total_joins_today)
            ),
            el('div', { className: 'stat-card' },
                el('div', { className: 'stat-label' }, '今日活跃用户'),
                el('div', { className: 'stat-value' }, stats.active_users_today_count)
            )
        );
        container.appendChild(statsGrid);

        // Pending chats approval section
        const pendingChats = chats.filter(c => c.authorizationStatus === 0 || c.authorizationStatus === 'Pending');
        
        const pendingSection = el('div', { className: 'card' },
            el('h2', {}, `待授权群聊 (${pendingChats.length})`)
        );

        if (pendingChats.length === 0) {
            pendingSection.appendChild(el('p', { style: { opacity: 0.7 } }, '暂无待处理的群聊授权请求。'));
        } else {
            const table = el('table', {},
                el('thead', {},
                    el('tr', {},
                        el('th', {}, '群组 ID'),
                        el('th', {}, '标题'),
                        el('th', {}, '类型'),
                        el('th', {}, '操作')
                    )
                )
            );
            const tbody = el('tbody', {});
            pendingChats.forEach(chat => {
                const btnApprove = el('button', {
                    onclick: async () => {
                        try {
                            await apiFetch(`/api/admin/chats/${chat.chatId}/approve`, { method: 'POST' });
                            render(container);
                        } catch (e) {
                            alert('批准失败: ' + e.message);
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
                            alert('拒绝并封禁失败: ' + e.message);
                        }
                    }
                }, '拒绝');

                const tdActions = el('td', {}, btnApprove, ' ', btnBlock);
                tbody.appendChild(
                    el('tr', {},
                        el('td', {}, chat.chatId.toString()),
                        el('td', {}, chat.title),
                        el('td', {}, chat.chatType),
                        tdActions
                    )
                );
            });
            table.appendChild(tbody);
            pendingSection.appendChild(el('div', { className: 'table-container' }, table));
        }
        container.appendChild(pendingSection);

    } catch (e) {
        container.textContent = '';
        container.appendChild(el('p', { className: 'error' }, '加载仪表盘失败: ' + e.message));
    }
}
