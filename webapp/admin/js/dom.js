export function el(tag, attrs = {}, ...children) {
    const element = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs)) {
        if (key.startsWith('on') && typeof value === 'function') {
            element.addEventListener(key.slice(2).toLowerCase(), value);
        } else if (key === 'className') {
            element.className = value;
        } else if (key === 'style' && typeof value === 'object') {
            Object.assign(element.style, value);
        } else if (key === 'disabled') {
            if (value) {
                element.setAttribute('disabled', 'disabled');
                element.disabled = true;
            } else {
                element.removeAttribute('disabled');
                element.disabled = false;
            }
        } else {
            element.setAttribute(key, value);
        }
    }
    for (const child of children) {
        if (typeof child === 'string' || typeof child === 'number') {
            element.appendChild(document.createTextNode(child));
        } else if (child instanceof HTMLElement) {
            element.appendChild(child);
        }
    }
    return element;
}

export function safeConfirm(message, requiredInput = null) {
    if (requiredInput) {
        const value = prompt(message + `\n请输入 "${requiredInput}" 进行二次确认：`);
        return value === requiredInput;
    }
    return confirm(message);
}
