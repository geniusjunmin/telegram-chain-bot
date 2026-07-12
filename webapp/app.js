const tg = window.Telegram?.WebApp;
tg?.ready();

const params = new URLSearchParams(window.location.search);
const publicId = params.get("chain_id") || "";

const titleEl = document.getElementById("title");
const listEl = document.getElementById("memberList");
const joinBtn = document.getElementById("joinBtn");
const leaveBtn = document.getElementById("leaveBtn");
const statusEl = document.getElementById("status");
const displayNameField = document.getElementById("displayNameField");
const displayNameInput = document.getElementById("displayName");

const user = tg?.initDataUnsafe?.user;
const hasTelegramInitData = Boolean(tg?.initData && user);

if (user) {
  displayNameInput.value = user.username || user.first_name || `user_${user.id}`;
}

if (!publicId) {
  joinBtn.disabled = true;
  displayNameInput.disabled = true;
  statusEl.textContent = "无法识别接龙信息";
} else if (!hasTelegramInitData) {
  joinBtn.disabled = true;
  displayNameInput.disabled = true;
  displayNameField.hidden = true;
  statusEl.textContent = "当前页面仅用于查看名单。群聊里请点击“私聊填写名字”后加入。";
}

async function loadChain() {
  if (!publicId) return;

  try {
    const resp = await fetch(`/api/chains/${publicId}`, {
      headers: {
        "X-Telegram-Init-Data": tg?.initData || ""
      }
    });

    if (!resp.ok) {
      statusEl.textContent = "接龙不存在";
      return;
    }

    const data = await resp.json();
    titleEl.textContent = `🍽 ${data.title}`;

    listEl.innerHTML = "";
    if (!data.members?.length) {
      const li = document.createElement("li");
      li.textContent = "";
      listEl.appendChild(li);
    } else {
      data.members.forEach((m) => {
        const li = document.createElement("li");
        li.textContent = m.displayName;
        listEl.appendChild(li);
      });
    }

    if (hasTelegramInitData) {
      if (data.hasJoined) {
        joinBtn.style.display = "none";
        leaveBtn.style.display = "block";
      } else {
        joinBtn.style.display = "block";
        leaveBtn.style.display = "none";
      }
    }
  } catch (err) {
    statusEl.textContent = "加载接龙失败，请重试。";
  }
}

async function joinChain() {
  if (!hasTelegramInitData) {
    statusEl.textContent = "当前页面不在 Telegram WebApp 中，无法识别你的身份。";
    return;
  }

  const displayName = displayNameInput.value.trim();
  if (!displayName) {
    statusEl.textContent = "请先填写显示名字";
    displayNameInput.focus();
    return;
  }

  joinBtn.disabled = true;
  displayNameInput.disabled = true;
  statusEl.textContent = "正在加入...";

  try {
    const body = {
      displayName: displayName
    };

    const resp = await fetch(`/api/chains/${publicId}/join`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Telegram-Init-Data": tg?.initData || ""
      },
      body: JSON.stringify(body)
    });

    if (!resp.ok) {
      statusEl.textContent = "加入失败，请重试";
      joinBtn.disabled = false;
      displayNameInput.disabled = false;
      return;
    }

    const result = await resp.json();
    statusEl.textContent = result.joined ? "加入成功" : "名字已更新";
    await loadChain();
  } catch (err) {
    statusEl.textContent = "请求发生错误，请重试";
  } finally {
    joinBtn.disabled = false;
    displayNameInput.disabled = false;
  }
}

async function leaveChain() {
  if (!hasTelegramInitData) {
    statusEl.textContent = "当前页面不在 Telegram WebApp 中，无法识别你的身份。";
    return;
  }

  leaveBtn.disabled = true;
  displayNameInput.disabled = true;
  statusEl.textContent = "正在退出...";

  try {
    const resp = await fetch(`/api/chains/${publicId}/leave`, {
      method: "POST",
      headers: {
        "X-Telegram-Init-Data": tg?.initData || ""
      }
    });

    if (!resp.ok) {
      statusEl.textContent = "退出失败，请重试";
      leaveBtn.disabled = false;
      displayNameInput.disabled = false;
      return;
    }

    statusEl.textContent = "已退出接龙";
    await loadChain();
  } catch (err) {
    statusEl.textContent = "请求发生错误，请重试";
  } finally {
    leaveBtn.disabled = false;
    displayNameInput.disabled = false;
  }
}

joinBtn.addEventListener("click", joinChain);
leaveBtn.addEventListener("click", leaveChain);

loadChain();
