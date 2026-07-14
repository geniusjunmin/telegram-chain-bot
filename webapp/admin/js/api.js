export function getCookie(name) {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop().split(';').shift();
    return null;
}

export async function apiFetch(url, options = {}) {
    const method = options.method || 'GET';
    let csrfToken = getCookie('XSRF-TOKEN');
    if (!csrfToken && method !== 'GET') {
        try {
            await fetch('/api/admin/csrf');
            csrfToken = getCookie('XSRF-TOKEN');
        } catch (e) {
            console.error("Failed to fetch CSRF token", e);
        }
    }

    options.headers = {
        ...options.headers,
        'Content-Type': 'application/json'
    };
    if (csrfToken && method !== 'GET') {
        options.headers['X-XSRF-TOKEN'] = csrfToken;
    }
    options.credentials = 'same-origin';
    
    const response = await fetch(url, options);
    if (response.status === 401) {
        localStorage.removeItem('adminRole');
        localStorage.removeItem('adminPermissions');
        window.dispatchEvent(new CustomEvent('auth-required'));
        throw new Error('Unauthorized');
    }
    if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || '请求失败');
    }
    return response.json().catch(() => null);
}
