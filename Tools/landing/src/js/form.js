// Lead form. The wire contract is v1's, unchanged: form-encoded POST (no CORS preflight)
// to the LandingLead webhook with exactly name, phone, website (honeypot), source=landing;
// the webhook answers {success:true} or {success:false,error:"bad_request"|…}.
export function validatePhone(value) {
  const digits = (String(value || '').match(/\d/g) || []).length;
  return { ok: digits >= 10 && digits <= 15, digits };
}

export function encodeLead(fields) {
  const p = new URLSearchParams();
  p.set('name', String(fields.name || '').trim());
  p.set('phone', String(fields.phone || '').trim());
  p.set('website', String(fields.website || ''));
  p.set('source', 'landing');
  return p;
}

export async function submitLead(endpoint, fields, fetchImpl) {
  if (!validatePhone(fields.phone).ok) return { ok: false, reason: 'bad_phone' };
  try {
    const r = await fetchImpl(endpoint, { method: 'POST', body: encodeLead(fields) });
    const j = await r.json();
    if (j && j.success) return { ok: true };
    return { ok: false, reason: j && j.error === 'bad_request' ? 'bad_phone' : 'server' };
  } catch {
    return { ok: false, reason: 'network' };
  }
}

const MESSAGES = {
  bad_phone: 'Проверьте номер телефона — нужно от 10 до 15 цифр.',
  server: 'Не получилось отправить. Попробуйте ещё раз чуть позже.',
  network: 'Нет связи с сервером. Проверьте интернет и попробуйте ещё раз.',
};

// Wires the markup of section 90. Runs on every quality tier — this is the conversion
// point, nothing here depends on GSAP or on the motion tier. With JS the form validates
// itself (friendly RU message in #leadErr instead of the browser bubble); without JS the
// form still posts natively to its action (same endpoint, same form encoding).
export function initForm(form, { onSuccess } = {}) {
  if (!form || form.dataset.bound) return;
  form.dataset.bound = '1';
  const btn = form.querySelector('#leadBtn'), err = form.querySelector('#leadErr');
  const ok = document.getElementById('leadOk'), phone = form.querySelector('#leadPhone');
  if (!btn || !err || !ok || !phone) return;
  form.noValidate = true;
  const face = btn.querySelector('.bt') || btn; // keeps the button's icon/spinner nodes intact
  const label = face.textContent;
  const field = phone.closest('.fld');
  const markPhone = (bad) => {
    if (bad) phone.setAttribute('aria-invalid', 'true'); else phone.removeAttribute('aria-invalid');
  };
  const showErr = (text) => { err.textContent = text; err.hidden = false; };

  phone.addEventListener('input', () => {
    const v = validatePhone(phone.value);
    if (field) field.classList.toggle('is-ok', v.ok);
    if (v.ok) { markPhone(false); err.hidden = true; }
  });

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (btn.disabled) return;
    err.hidden = true;
    const fields = Object.fromEntries(new FormData(form));
    if (!validatePhone(fields.phone).ok) {
      showErr(MESSAGES.bad_phone); markPhone(true); nudge(field); phone.focus();
      return;
    }
    btn.disabled = true; btn.classList.add('is-busy'); face.textContent = 'Отправляем…';
    const r = await submitLead(form.dataset.endpoint, fields, (u, i) => fetch(u, i));
    if (r.ok) {
      form.hidden = true; ok.hidden = false; ok.focus({ preventScroll: true });
      if (onSuccess) onSuccess();
      return;
    }
    showErr(MESSAGES[r.reason] || MESSAGES.server);
    btn.disabled = false; btn.classList.remove('is-busy'); face.textContent = label;
    if (r.reason === 'bad_phone') { markPhone(true); nudge(field); phone.focus(); }
  });
}

// A short sideways shake on the offending field — a CSS keyframe (transform only),
// restarted by re-adding the class. Reduced motion is handled by the base stylesheet.
function nudge(el) {
  if (!el) return;
  el.classList.remove('is-bad');
  void el.offsetWidth; // restart the animation
  el.classList.add('is-bad');
}
