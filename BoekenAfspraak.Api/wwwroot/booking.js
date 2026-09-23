(function () {
  let CONFIG = { slotTimes: [], minBooks: 10, daysAhead: 14 };
  let days = [];
  let selectedDayIdx = 0;
  let selectedTime = null;
  let availabilityCache = {};

  const daysEl = document.getElementById("days");
  const slotsEl = document.getElementById("slots");
  const priceValueEl = document.getElementById("price-value");

  function dateKey(d) {
    return d.getFullYear() + "-" + String(d.getMonth()+1).padStart(2,"0") + "-" + String(d.getDate()).padStart(2,"0");
  }

  async function loadConfig() {
    const res = await fetch("/api/config");
    CONFIG = await res.json();
    document.getElementById("hero-min").textContent = CONFIG.minBooks;
    document.getElementById("min-hint").textContent = "(minimaal " + CONFIG.minBooks + ")";
    document.getElementById("aantal").min = CONFIG.minBooks;

    const ownerEmailLink = document.getElementById("owner-email-link");
    if (ownerEmailLink && CONFIG.ownerEmail) {
      ownerEmailLink.textContent = CONFIG.ownerEmail;
      ownerEmailLink.href = "mailto:" + CONFIG.ownerEmail;
    }

    const base = new Date(); base.setHours(0,0,0,0);
    days = [];
    for (let i = 0; i < CONFIG.daysAhead; i++) {
      const d = new Date(base); d.setDate(base.getDate() + i); days.push(d);
    }
    renderDays();
    await loadAvailability(days[selectedDayIdx]);
    renderSlots();
  }

  async function loadAvailability(d) {
    const key = dateKey(d);
    if (availabilityCache[key]) return availabilityCache[key];
    const res = await fetch("/api/availability?date=" + encodeURIComponent(key));
    const data = await res.json();
    availabilityCache[key] = data;
    return data;
  }

  function renderDays() {
    daysEl.innerHTML = "";
    days.forEach((d, idx) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "day-pill" + (idx === selectedDayIdx ? " active" : "");
      const wk = d.toLocaleDateString("nl-NL", { weekday: "short" });
      const dm = d.toLocaleDateString("nl-NL", { day: "numeric", month: "short" });
      btn.innerHTML = '<span class="d">' + wk + '</span><span>' + dm + '</span>';
      btn.addEventListener("click", async () => {
        selectedDayIdx = idx;
        selectedTime = null;
        renderDays();
        await loadAvailability(days[idx]);
        renderSlots();
      });
      daysEl.appendChild(btn);
    });
  }

  function renderSlots() {
    slotsEl.innerHTML = "";
    const d = days[selectedDayIdx];
    const data = availabilityCache[dateKey(d)];
    if (!data) { slotsEl.innerHTML = '<div class="slots-empty">Bezig met laden…</div>'; return; }

    const anyAvailable = data.slots.some(s => s.available);
    if (!anyAvailable) {
      slotsEl.innerHTML = '<div class="slots-empty">Deze dag is volgeboekt. Kies een andere dag hierboven.</div>';
      return;
    }

    data.slots.forEach(s => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "slot" + (selectedTime === s.time ? " active" : "");
      btn.textContent = s.time;
      if (!s.available) {
        btn.disabled = true;
      } else {
        btn.addEventListener("click", () => { selectedTime = s.time; renderSlots(); });
      }
      slotsEl.appendChild(btn);
    });
  }

  // --- Price preview (flat estimate) ---
  function updatePricePreview() {
    const count = parseInt(document.getElementById("aantal").value, 10) || 0;
    const blocks = Math.floor(count / 10);
    priceValueEl.textContent = "€ " + (blocks * 3.5).toFixed(2).replace(".", ",");
  }
  document.getElementById("aantal").addEventListener("input", updatePricePreview);

  // --- Submit ---
  function setInvalid(id, invalid) {
    document.getElementById(id).classList.toggle("invalid", invalid);
  }

  // --- Address lookup (PDOK Locatieserver, free, no key, CORS-enabled) ---
  const postcodeEl = document.getElementById("postcode");
  const huisnummerEl = document.getElementById("huisnummer");
  const adresGevondenEl = document.getElementById("adres-gevonden");
  const adresNietGevondenEl = document.getElementById("adres-niet-gevonden");
  const adresHandmatigEl = document.getElementById("adres-handmatig");
  const adresFinalEl = document.getElementById("adres-final");

  function normalizePostcode(raw) {
    return raw.replace(/\s+/g, "").toUpperCase();
  }
  function isValidPostcode(normalized) {
    return /^\d{4}[A-Z]{2}$/.test(normalized);
  }

  function showNotFound() {
    adresGevondenEl.style.display = "none";
    adresNietGevondenEl.style.display = "block";
    adresHandmatigEl.style.display = "block";
    adresFinalEl.value = adresHandmatigEl.value.trim();
  }
  function showFound(weergavenaam) {
    adresGevondenEl.textContent = weergavenaam;
    adresGevondenEl.style.display = "block";
    adresNietGevondenEl.style.display = "none";
    adresHandmatigEl.style.display = "none";
    adresFinalEl.value = weergavenaam;
  }
  function resetAddressResult() {
    adresGevondenEl.style.display = "none";
    adresNietGevondenEl.style.display = "none";
    adresHandmatigEl.style.display = "none";
    adresFinalEl.value = "";
  }

  let addressLookupTimer = null;
  async function lookupAddress() {
    const postcode = normalizePostcode(postcodeEl.value);
    const huisnummer = huisnummerEl.value.trim();
    if (!isValidPostcode(postcode) || !huisnummer) {
      resetAddressResult();
      return;
    }
    try {
      const url = "https://api.pdok.nl/bzk/locatieserver/search/v3_1/free?q=" +
        encodeURIComponent(postcode + " " + huisnummer) + "&fq=type:adres&rows=1";
      const res = await fetch(url);
      if (!res.ok) { showNotFound(); return; }
      const data = await res.json();
      const doc = data && data.response && data.response.docs && data.response.docs[0];
      if (doc && doc.weergavenaam) {
        showFound(doc.weergavenaam);
      } else {
        showNotFound();
      }
    } catch (e) {
      showNotFound();
    }
  }
  function scheduleAddressLookup() {
    clearTimeout(addressLookupTimer);
    addressLookupTimer = setTimeout(lookupAddress, 400);
  }
  postcodeEl.addEventListener("input", scheduleAddressLookup);
  huisnummerEl.addEventListener("input", scheduleAddressLookup);
  adresHandmatigEl.addEventListener("input", () => {
    adresFinalEl.value = adresHandmatigEl.value.trim();
  });

  document.getElementById("submit-btn").addEventListener("click", async function () {
    const naam = document.getElementById("naam").value.trim();
    const adres = document.getElementById("adres-final").value.trim();
    const email = document.getElementById("email").value.trim();
    const telefoon = document.getElementById("telefoon").value.trim();
    const aantal = document.getElementById("aantal").value.trim();
    const soort = document.getElementById("soort").value.trim();
    const formErr = document.getElementById("form-err");
    formErr.style.display = "none";

    let ok = true;
    setInvalid("f-naam", !naam); if (!naam) ok = false;
    setInvalid("f-adres-resultaat", !adres); if (!adres) ok = false;
    const emailOk = /\S+@\S+\.\S+/.test(email);
    setInvalid("f-email", !emailOk); if (!emailOk) ok = false;
    const aantalNum = parseInt(aantal, 10);
    const aantalInvalid = !aantal || isNaN(aantalNum) || aantalNum < CONFIG.minBooks;
    setInvalid("f-aantal", aantalInvalid); if (aantalInvalid) ok = false;

    if (!selectedTime) {
      formErr.textContent = "Kies eerst een dag en tijd hierboven.";
      formErr.style.display = "block";
      ok = false;
    }
    if (!ok) return;

    const submitBtn = document.getElementById("submit-btn");
    submitBtn.disabled = true;
    submitBtn.textContent = "Bezig...";

    const body = {
      name: naam, address: adres, email, phone: telefoon || null,
      bookCount: aantalNum, bookType: soort || null,
      date: dateKey(days[selectedDayIdx]), timeSlot: selectedTime,
      honeypot: document.getElementById("website").value
    };

    try {
      const res = await fetch("/api/appointments", {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body)
      });

      if (res.status === 409) {
        formErr.textContent = "Dit tijdstip is net bezet geraakt. Kies een ander moment.";
        formErr.style.display = "block";
        delete availabilityCache[dateKey(days[selectedDayIdx])];
        await loadAvailability(days[selectedDayIdx]);
        selectedTime = null;
        renderSlots();
        return;
      }
      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        formErr.textContent = err.error || "Er ging iets mis. Probeer het opnieuw.";
        formErr.style.display = "block";
        return;
      }

      const result = await res.json();

      const photoInput = document.getElementById("foto");
      if (photoInput.files.length > 0) {
        const fd = new FormData();
        Array.from(photoInput.files).slice(0, 5).forEach(f => fd.append("files", f));
        await fetch(`/api/appointments/manage/${result.manageToken}/photos`, { method: "POST", body: fd }).catch(() => {});
      }

      document.getElementById("confirm-text").textContent =
        `Uw afspraak staat gepland voor ${days[selectedDayIdx].toLocaleDateString("nl-NL", { weekday: "long", day: "numeric", month: "long" })} om ${selectedTime}. Geschatte prijsindicatie: € ${result.estimatedPriceEuro.toFixed(2).replace(".", ",")}.`;
      document.getElementById("manage-link").href = `/beheer.html?token=${result.manageToken}`;
      document.getElementById("confirm").style.display = "block";
      document.getElementById("confirm").scrollIntoView({ behavior: "smooth", block: "start" });
      submitBtn.textContent = "Aangevraagd";
    } catch (e) {
      formErr.textContent = "Kon geen verbinding maken. Probeer het opnieuw.";
      formErr.style.display = "block";
      submitBtn.disabled = false;
      submitBtn.textContent = "Afspraak aanvragen";
    }
  });

  loadConfig();
})();
