(function () {
  const TOKEN_KEY = "boeken_admin_token";
  const loginCard = document.getElementById("login-card");
  const panelCard = document.getElementById("panel-card");
  const tbody = document.getElementById("table-body");

  function getToken() { return sessionStorage.getItem(TOKEN_KEY); }
  function setToken(t) { sessionStorage.setItem(TOKEN_KEY, t); }
  function clearToken() { sessionStorage.removeItem(TOKEN_KEY); }

  async function api(path, opts = {}) {
    const headers = Object.assign({}, opts.headers, { Authorization: "Bearer " + getToken() });
    const res = await fetch(path, Object.assign({}, opts, { headers }));
    if (res.status === 401) { clearToken(); showLogin(); throw new Error("unauthorized"); }
    return res;
  }

  function showLogin() { loginCard.style.display = "block"; panelCard.style.display = "none"; }
  function showPanel() { loginCard.style.display = "none"; panelCard.style.display = "block"; loadAppointments(); }

  document.getElementById("login-btn").addEventListener("click", async () => {
    const email = document.getElementById("email").value.trim();
    const password = document.getElementById("password").value;
    const errEl = document.getElementById("login-err");
    errEl.style.display = "none";

    const res = await fetch("/api/admin/login", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password })
    });
    if (!res.ok) { errEl.textContent = "Onjuiste inloggegevens."; errEl.style.display = "block"; return; }
    const data = await res.json();
    setToken(data.token);
    showPanel();
  });

  document.getElementById("logout-btn").addEventListener("click", () => { clearToken(); showLogin(); });

  document.getElementById("status-filter").addEventListener("change", loadAppointments);
  document.getElementById("export-btn").addEventListener("click", async () => {
    const res = await api("/api/admin/appointments/export.csv");
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = "afspraken.csv"; a.click();
    URL.revokeObjectURL(url);
  });

  async function loadAppointments() {
    const status = document.getElementById("status-filter").value;
    const res = await api("/api/admin/appointments" + (status ? "?status=" + status : ""));
    const list = await res.json();
    renderTable(list);
  }

  function renderTable(list) {
    tbody.innerHTML = "";
    list.forEach(a => {
      const tr = document.createElement("tr");
      const statusClass = a.status === "Confirmed" ? "confirmed" : "cancelled";
      const statusLabel = a.status === "Confirmed" ? "Bevestigd" : "Geannuleerd";
      tr.innerHTML = `
        <td>${a.date}</td>
        <td>${a.timeSlot}</td>
        <td><span class="status-tag ${statusClass}">${statusLabel}</span></td>
        <td>${escapeHtml(a.name)}</td>
        <td>${escapeHtml(a.address)}</td>
        <td>${escapeHtml(a.email)}</td>
        <td>${escapeHtml(a.phone || "-")}</td>
        <td>${a.bookCount}</td>
        <td>€ ${a.estimatedPriceEuro.toFixed(2).replace(".", ",")}</td>
        <td>${a.rescheduleCount > 0 ? a.rescheduleCount + "×" : "-"}</td>
        <td></td>
      `;
      if (a.status === "Confirmed") {
        const btn = document.createElement("button");
        btn.className = "btn-secondary";
        btn.textContent = "Annuleren";
        btn.addEventListener("click", async () => {
          if (!confirm(`Afspraak van ${a.name} annuleren?`)) return;
          await api(`/api/admin/appointments/${a.id}/cancel`, { method: "POST" });
          loadAppointments();
        });
        tr.lastElementChild.appendChild(btn);
      }
      if (a.photoFileNames && a.photoFileNames.length > 0) {
        const photoBtn = document.createElement("button");
        photoBtn.className = "btn-secondary";
        photoBtn.style.marginLeft = "6px";
        photoBtn.textContent = `Foto's (${a.photoFileNames.length})`;
        photoBtn.addEventListener("click", () => showPhotos(a.id, a.photoFileNames));
        tr.lastElementChild.appendChild(photoBtn);
      }
      tbody.appendChild(tr);
    });
  }

  // Photos live behind an admin-only endpoint, so they're fetched with the
  // Authorization header (via api()) and shown as blob object URLs — the
  // JWT never ends up in an <img src> or address bar.
  async function showPhotos(appointmentId, fileNames) {
    const overlay = document.createElement("div");
    overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.7);z-index:1000;display:flex;align-items:center;justify-content:center;padding:20px;";
    const box = document.createElement("div");
    box.style.cssText = "background:var(--paper);border-radius:10px;padding:20px;max-width:90vw;max-height:90vh;overflow:auto;";
    const closeBtn = document.createElement("button");
    closeBtn.className = "btn-secondary";
    closeBtn.textContent = "Sluiten";
    closeBtn.style.marginBottom = "12px";
    const objectUrls = [];
    function close() {
      objectUrls.forEach(u => URL.revokeObjectURL(u));
      overlay.remove();
    }
    closeBtn.addEventListener("click", close);
    overlay.addEventListener("click", (e) => { if (e.target === overlay) close(); });
    box.appendChild(closeBtn);

    const grid = document.createElement("div");
    grid.style.cssText = "display:flex;flex-wrap:wrap;gap:12px;";
    box.appendChild(grid);
    overlay.appendChild(box);
    document.body.appendChild(overlay);

    for (const fileName of fileNames) {
      try {
        const res = await api(`/api/admin/appointments/${appointmentId}/photos/${encodeURIComponent(fileName)}`);
        if (!res.ok) continue;
        const blob = await res.blob();
        const url = URL.createObjectURL(blob);
        objectUrls.push(url);
        const link = document.createElement("a");
        link.href = url;
        link.target = "_blank";
        const img = document.createElement("img");
        img.src = url;
        img.style.cssText = "max-width:220px;max-height:220px;border-radius:8px;display:block;";
        link.appendChild(img);
        grid.appendChild(link);
      } catch (e) {
        // Best-effort: skip a photo that failed to load.
      }
    }
  }

  function escapeHtml(s) {
    const d = document.createElement("div");
    d.textContent = s;
    return d.innerHTML;
  }

  if (getToken()) showPanel(); else showLogin();
})();
