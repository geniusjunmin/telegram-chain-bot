import { el } from '../dom.js';
import { apiFetch } from '../api.js';

let currentPage = 1;
const pageSize = 15;
let currentAction = '';
let currentSuccess = '';

export async function render(container) {
    container.textContent = '';
    const loading = el('h2', {}, '加载审计日志中...');
    container.appendChild(loading);

    try {
        let url = `/api/admin/audit-logs?page=${currentPage}&pageSize=${pageSize}`;
        if (currentAction) {
            url += `&action=${encodeURIComponent(currentAction)}`;
        }
        if (currentSuccess !== '') {
            url += `&success=${currentSuccess}`;
        }

        const data = await apiFetch(url);
        container.textContent = '';

        const h1 = el('h1', {}, '系统审计日志');
        container.appendChild(h1);

        // Filter block
        const inputAction = el('input', {
            type: 'text',
            placeholder: '按操作名过滤 (如 Login, UpdateChat)',
            value: currentAction,
            style: { width: '250px', marginRight: '10px', marginBottom: '0' }
        });
        const selectSuccess = el('select', {
            style: { width: '150px', marginRight: '10px', marginBottom: '0' }
        },
            el('option', { value: '', selected: currentSuccess === '' }, '全部状态'),
            el('option', { value: 'true', selected: currentSuccess === 'true' }, '成功'),
            el('option', { value: 'false', selected: currentSuccess === 'false' }, '失败')
        );
        const btnFilter = el('button', {
            onclick: () => {
                currentAction = inputAction.value.trim();
                currentSuccess = selectSuccess.value;
                currentPage = 1;
                render(container);
            }
        }, '筛选');

        const filterCard = el('div', {
            className: 'card',
            style: { display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' }
        }, inputAction, selectSuccess, btnFilter);
        container.appendChild(filterCard);

        if (!data.items || data.items.length === 0) {
            container.appendChild(el('p', { style: { opacity: 0.7 } }, '暂无审计日志。'));
            return;
        }

        const table = el('table', {},
            el('thead', {},
                el('tr', {},
                    el('th', {}, '时间'),
                    el('th', {}, '操作'),
                    el('th', {}, '执行人'),
                    el('th', {}, 'IP 哈希'),
                    el('th', {}, '结果'),
                    el('th', {}, '详情')
                )
            )
        );

        const tbody = el('tbody', {});
        data.items.forEach(log => {
            let actorText = log.actorType;
            if (log.actorAdminUsername) {
                actorText += ` (${log.actorAdminUsername})`;
            } else if (log.actorTelegramUserId) {
                actorText += ` (TG: ${log.actorTelegramUserId})`;
            }

            const statusCell = el('span', {
                style: { color: log.success ? 'var(--success)' : 'var(--danger)', fontWeight: 'bold' }
            }, log.success ? '成功' : '失败');

            tbody.appendChild(
                el('tr', {},
                    el('td', {}, new Date(log.createdAt).toLocaleString()),
                    el('td', {}, log.action),
                    el('td', {}, actorText),
                    el('td', { style: { fontFamily: 'monospace', fontSize: '12px' } }, log.ipAddressHash || '-'),
                    el('td', {}, statusCell),
                    el('td', {}, log.failureReason || '-')
                )
            );
        });
        table.appendChild(tbody);
        container.appendChild(el('div', { className: 'table-container' }, table));

        // Pagination buttons
        const totalPages = Math.ceil(data.total / pageSize);
        const btnPrev = el('button', {
            className: 'secondary',
            disabled: currentPage <= 1,
            onclick: () => {
                if (currentPage > 1) {
                    currentPage--;
                    render(container);
                }
            }
        }, '上一页');

        const btnNext = el('button', {
            className: 'secondary',
            disabled: currentPage >= totalPages,
            onclick: () => {
                if (currentPage < totalPages) {
                    currentPage++;
                    render(container);
                }
            }
        }, '下一页');

        const paginationInfo = el('span', { style: { alignSelf: 'center' } }, `第 ${currentPage} / ${totalPages} 页 (共 ${data.total} 条)`);

        const paginationBlock = el('div', {
            style: { display: 'flex', gap: '15px', marginTop: '20px', justifyContent: 'center' }
        }, btnPrev, paginationInfo, btnNext);

        container.appendChild(paginationBlock);

    } catch (e) {
        container.textContent = '';
        container.appendChild(el('p', { className: 'error' }, '加载审计日志失败: ' + e.message));
    }
}
