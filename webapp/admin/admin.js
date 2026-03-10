let adminToken = localStorage.getItem('adminToken');

if (adminToken) {
    showAdminPage();
}

async function apiFetch(url, options = {}) {
    options.headers = {
        ...options.headers,
        'X-Admin-Token': adminToken,
        'Content-Type': 'application/json'
    };
    
    const response = await fetch(url, options);
    if (response.status === 401) {
        logout();
        throw new Error('Unauthorized');
    }
    if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || 'Request failed');
    }
    return response.json().catch(() => null);
}

async function login() {
    const username = document.getElementById('username').value;
    const password = document.getElementById('password').value;
    const errorEl = document.getElementById('login-error');
    
    try {
        const data = await apiFetch('/api/admin/login', {
            method: 'POST',
            body: JSON.stringify({ username, password })
        });
        
        adminToken = data.token;
        localStorage.setItem('adminToken', adminToken);
        showAdminPage();
    } catch (e) {
        errorEl.textContent = '登录失败: ' + e.message;
    }
}

function logout() {
    adminToken = null;
    localStorage.removeItem('adminToken');
    document.getElementById('login-page').style.display = 'block';
    document.getElementById('admin-page').style.display = 'none';
}

function showAdminPage() {
    document.getElementById('login-page').style.display = 'none';
    document.getElementById('admin-page').style.display = 'block';
    showChains();
}

async function showChains() {
    const content = document.getElementById('content');
    content.innerHTML = '<h2>加载中...</h2>';
    
    try {
        const chains = await apiFetch('/api/admin/chains');
        let html = `
            <div class="header-action">
                <h1>所有接龙</h1>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>ID</th>
                        <th>标题</th>
                        <th>创建时间</th>
                        <th>操作</th>
                    </tr>
                </thead>
                <tbody>
        `;
        
        chains.forEach(c => {
            html += `
                <tr>
                    <td>${c.id}</td>
                    <td>${c.title}</td>
                    <td>${new Date(c.createdAt).toLocaleString()}</td>
                    <td>
                        <button onclick="viewMembers(${c.id}, '${c.title.replace(/'/g, "\\'")}')">查看成员</button>
                        <button class="danger" onclick="deleteChain(${c.id})">删除</button>
                    </td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        content.innerHTML = html;
    } catch (e) {
        content.innerHTML = `<p class="error">加载失败: ${e.message}</p>`;
    }
}

async function deleteChain(id) {
    if (!confirm('确定要删除这个接龙吗？所有成员数据都将被清除！')) return;
    try {
        await apiFetch(`/api/admin/chains/${id}`, { method: 'DELETE' });
        showChains();
    } catch (e) {
        alert('删除失败: ' + e.message);
    }
}

async function viewMembers(chainId, title) {
    const content = document.getElementById('content');
    content.innerHTML = `<h2>正在获取 [${title}] 的成员...</h2>`;
    
    try {
        const members = await apiFetch(`/api/admin/chains/${chainId}/members`);
        let html = `
            <div class="header-action">
                <h1>接龙成员: ${title}</h1>
                <button class="secondary" onclick="showChains()">返回列表</button>
            </div>
            <table>
                <thead>
                    <tr>
                        <th>ID</th>
                        <th>用户名</th>
                        <th>TG 昵称</th>
                        <th>加入时间</th>
                        <th>操作</th>
                    </tr>
                </thead>
                <tbody>
        `;
        
        members.forEach(m => {
            html += `
                <tr>
                    <td>${m.id}</td>
                    <td>${m.username}</td>
                    <td>${m.telegramNickname || '-'}</td>
                    <td>${new Date(m.joinTime).toLocaleString()}</td>
                    <td>
                        <button onclick="openEditMember(${m.id}, '${m.username.replace(/'/g, "\\'")}', '${(m.telegramNickname || '').replace(/'/g, "\\'")}')">修改</button>
                        <button class="danger" onclick="deleteMember(${m.id}, ${chainId}, '${title.replace(/'/g, "\\'")}')">删除</button>
                    </td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        content.innerHTML = html;
    } catch (e) {
        content.innerHTML = `<p class="error">加载失败: ${e.message}</p>`;
    }
}

async function deleteMember(id, chainId, title) {
    if (!confirm('确定要从接龙中移除该成员吗？')) return;
    try {
        await apiFetch(`/api/admin/members/${id}`, { method: 'DELETE' });
        viewMembers(chainId, title);
    } catch (e) {
        alert('删除失败: ' + e.message);
    }
}

function openEditMember(id, username, nickname) {
    document.getElementById('edit-member-id').value = id;
    document.getElementById('edit-username').value = username;
    document.getElementById('edit-nickname').value = nickname;
    document.getElementById('edit-modal').style.display = 'flex';
}

function closeModal() {
    document.getElementById('edit-modal').style.display = 'none';
}

async function saveMember() {
    const id = document.getElementById('edit-member-id').value;
    const username = document.getElementById('edit-username').value;
    const telegramNickname = document.getElementById('edit-nickname').value;
    
    try {
        await apiFetch(`/api/admin/members/${id}`, {
            method: 'PUT',
            body: JSON.stringify({ username, telegramNickname })
        });
        closeModal();
        // 刷新当前视图，由于我们没有保存当前 chainId，简单做法是返回列表或者重新加载
        showChains(); 
    } catch (e) {
        alert('保存失败: ' + e.message);
    }
}

function showChangePassword() {
    const content = document.getElementById('content');
    content.innerHTML = `
        <div class="change-pw-container card">
            <h1>修改管理员密码</h1>
            <div class="form-group">
                <label>旧密码:</label>
                <input type="password" id="old-password">
            </div>
            <div class="form-group">
                <label>新密码:</label>
                <input type="password" id="new-password">
            </div>
            <div class="form-group">
                <label>确认新密码:</label>
                <input type="password" id="confirm-password">
            </div>
            <button onclick="updatePassword()">确认修改</button>
            <p id="pw-error" class="error"></p>
        </div>
    `;
}

async function updatePassword() {
    const oldPassword = document.getElementById('old-password').value;
    const newPassword = document.getElementById('new-password').value;
    const confirmPassword = document.getElementById('confirm-password').value;
    const errorEl = document.getElementById('pw-error');
    
    if (newPassword !== confirmPassword) {
        errorEl.textContent = '两次输入的新密码不一致';
        return;
    }
    
    try {
        await apiFetch('/api/admin/change-password', {
            method: 'POST',
            body: JSON.stringify({ oldPassword, newPassword })
        });
        alert('密码修改成功，请重新登录');
        logout();
    } catch (e) {
        errorEl.textContent = '修改失败: ' + e.message;
    }
}
