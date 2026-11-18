const $ = (s) => document.querySelector(s);
const $$ = (s) => [...document.querySelectorAll(s)];
const state = { mode: 'video', quality: '720p', height: 720, availableQualities: null, pro: false, isAdmin: false, adminToken: '', pendingLicense: '', reward: 0, downloads: [], user: null, resolvingThumb: null, resolvingTitle: null, resolvingProvider: null };
function adminHeaders(extra){ const h = { ...(extra||{}) }; if (state.adminToken) h['X-Admin-Token'] = state.adminToken; return h; }
const modalLayer = $('#modalLayer'), modalContent = $('#modalContent');
let resolveTimer = 0, resolvingUrl = '';
const defaultQualityMarkup = $('#qualityOptions').innerHTML;

function toast(message) { const old = $('.toast'); if (old) old.remove(); const el = document.createElement('div'); el.className = 'toast'; el.textContent = message; document.body.append(el); setTimeout(() => el.remove(), 3500); }
function showModal(html) { modalContent.innerHTML = html; modalLayer.classList.remove('hidden'); $('#modalClose')?.focus(); }
function closeModal() { modalLayer.classList.add('hidden'); modalContent.innerHTML = ''; }
function escapeHtml(s='') { return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }

function updatePlan() {
  $$('.quality-card').forEach(card => {
    const h = Number(card.dataset.height || ({'4K':2160,'2K':1440,'1080p':1080,'720p':720,'480p':480,'360p':360}[card.dataset.quality] || 0));
    const locked = h > 720;
    card.classList.toggle('locked', locked && !state.pro && !state.isAdmin);
    const first = card.querySelector('span');
    if (!first) return;
    const label = card.dataset.quality;
    if (locked && !state.pro && !state.isAdmin) first.innerHTML = `${label} <i>✦ PRO</i>`;
    else first.innerHTML = `${label}${label==='720p' && !locked ? ' <em>ÜCRETSİZ</em>' : ''}`;
  });
  $('#adProgressText') && ($('#adProgressText').textContent = `${state.reward} / 4 reklam izlendi`);
  $$('.ad-dots i').forEach((dot, i) => dot.classList.toggle('done', i < state.reward));
  const isPro = state.pro || state.isAdmin;
  if ($('#planBadge')) $('#planBadge').textContent = state.isAdmin ? 'ADMIN' : (isPro ? 'PRO' : 'STANDART');
  if ($('#planName')) $('#planName').textContent = state.isAdmin ? 'Yönetici' : (state.user?.proLifetime ? 'Sınırsız Pro' : (isPro ? 'Pro plan' : 'Standart plan'));
  if ($('#planDetail')) $('#planDetail').textContent = state.isAdmin ? 'Tüm kalite seçenekleri açık' : (isPro ? (state.user?.proLifetime ? 'Ömür boyu erişim' : `Bitiş: ${state.user?.proExpiresAt ? new Date(state.user.proExpiresAt).toLocaleDateString('tr-TR') : '-'}`) : '720p ve altı ücretsiz');
  if ($('#planQuality')) $('#planQuality').innerHTML = isPro ? '4K <small>ve altı</small>' : '720p <small>ve altı</small>';
  if ($('#planCaption')) $('#planCaption').textContent = state.isAdmin ? 'Admin hesabında sınırsız erişim' : (isPro ? 'Pro planda kullanılabilir' : 'Standart planda kullanılabilir');
  if ($('#watchAdsButton')) $('#watchAdsButton').classList.toggle('hidden', state.isAdmin || isPro);
  if ($('.ad-progress')) $('.ad-progress').classList.toggle('hidden', state.isAdmin || isPro);
  const name = state.user?.displayName || (state.isAdmin ? 'Yönetici' : 'Misafir kullanıcı');
  const sub = state.isAdmin ? 'Admin' : (isPro ? (state.user?.proLifetime ? 'Sınırsız Pro' : 'Pro') : 'Standart plan');
  $$('.profile b, .profile-top span:nth-child(2)').forEach(el => { if (el) el.textContent = name; });
  const sideSub = document.querySelector('.profile small'); if (sideSub) sideSub.textContent = sub;
  $$('.avatar').forEach(a => { if (a && !a.querySelector('img')) a.textContent = (name[0]||'M').toUpperCase(); });
}

function setQuality(q, height) {
  state.quality = q; state.height = Number(height) || 720;
  $$('.quality-card').forEach(c => {
    const selected = Number(c.dataset.height || 0) === state.height;
    c.classList.toggle('selected', selected);
    const mark = c.querySelector('.check'); if (mark) mark.remove();
    if (selected && !c.classList.contains('locked')) c.insertAdjacentHTML('beforeend', '<span class="check">✓</span>');
  });
}

function renderQualities(qualities, errorMessage = '') {
  state.availableQualities = qualities;
  const grid = $('#qualityOptions');
  if (!Array.isArray(qualities) || qualities.length === 0) { grid.innerHTML = `<div class="quality-empty">${escapeHtml(errorMessage||'Bu bağlantı için çözünürlük bilgisi alınamadı.')}</div>`; return; }
  const tiers = new Map();
  for (const item of qualities) { const h=Number(item.height); if(!Number.isFinite(h)||h<1) continue; const label=h>=2160?'4K':h>=1440?'2K':`${h}p`; const previous=tiers.get(label); if(!previous||h>previous.height) tiers.set(label,{label,height:h}); }
  const options=[...tiers.values()].sort((a,b)=>b.height-a.height);
  if (!options.length) { grid.innerHTML = '<div class="quality-empty">Bu video için video çözünürlüğü bulunamadı.</div>'; return; }
  grid.innerHTML=options.map(({label,height})=>`<button class="quality-card${height===state.height?' selected':''}" data-quality="${label}" data-height="${height}"><span>${label}</span><small>${height}p${height===720?' · HD':''}</small>${height===state.height?'<span class="check">✓</span>':''}</button>`).join('');
  if(!options.some(o=>o.height===state.height)){ const free=options.find(o=>o.height<=720)||options.at(-1); state.quality=free.label; state.height=free.height; setQuality(state.quality,state.height); }
  updatePlan();
}

function parseLink(raw) { try { const u = new URL((raw||'').trim()); if (!['http:','https:'].includes(u.protocol)) return null; return u; } catch { return null; } }
function fallbackVideoTitle(u){ const host=u.hostname.replace(/^www\./,''); if(/youtu\.be|youtube\.com/.test(host))return 'YouTube videosu'; if(/tiktok\.com/.test(host))return 'TikTok videosu'; if(/instagram\.com/.test(host))return 'Instagram gönderisi'; if(/x\.com|twitter\.com/.test(host))return 'X videosu'; let tail=u.pathname.split('/').filter(Boolean).pop()||'Video bağlantısı'; try{tail=decodeURIComponent(tail);}catch{} return tail.replace(/[-_]/g,' '); }

function inspectLink() {
  const u = parseLink($('#urlInput').value); const preview = $('#videoPreview');
  if (!u) { preview.classList.add('hidden'); resolvingUrl=''; state.availableQualities=null; state.quality='720p'; state.height=720; $('#qualityOptions').innerHTML=defaultQualityMarkup; state.resolvingThumb=null; state.resolvingTitle=null; state.resolvingProvider=null; updatePlan(); clearTimeout(resolveTimer); $('#previewImage').classList.add('hidden'); $('#previewPlay').classList.remove('hidden'); return; }
  if (resolvingUrl === u.href) return;
  resolvingUrl=u.href; state.availableQualities=null; $('#qualityOptions').innerHTML='<div class="quality-empty">Videonun kaliteleri aranıyor…</div>'; $('#previewHost').textContent='Video bilgisi yükleniyor…'; $('#previewTitle').textContent=fallbackVideoTitle(u); $('#previewProvider').textContent='VİDEO BAĞLANTISI'; $('#previewImage').classList.add('hidden'); $('#previewPlay').classList.remove('hidden'); preview.classList.remove('hidden'); clearTimeout(resolveTimer);
  resolveTimer=setTimeout(async()=>{
    try{
      const r=await fetch(`/api/video/resolve?url=${encodeURIComponent(u.href)}`,{credentials:'same-origin'});
      if(!r.ok) throw new Error('metadata');
      const data=await r.json();
      if(resolvingUrl!==u.href) return;
      renderQualities(data.qualities,data.formatError);
      state.resolvingTitle=data.title||fallbackVideoTitle(u);
      state.resolvingProvider=data.provider||'';
      state.resolvingThumb=data.thumbnailUrl||null;
      $('#previewTitle').textContent=state.resolvingTitle;
      $('#previewHost').textContent=data.author?`${data.author} · ${u.hostname.replace(/^www\./,'')}`:u.hostname.replace(/^www\./,'');
      $('#previewProvider').textContent=(data.provider||'VIDEO')+' · KAPAK';
      if(data.thumbnailUrl){ const img=$('#previewImage'); img.onload=()=>{img.classList.remove('hidden'); $('#previewPlay').classList.add('hidden');}; img.onerror=()=>{img.classList.add('hidden'); $('#previewPlay').classList.remove('hidden');}; img.src=data.thumbnailUrl; }
    } catch {
      if(resolvingUrl===u.href){
        renderQualities([],'Video bilgisi alınamadı. Sunucu/bağlantı kontrol edin.');
        $('#previewTitle').textContent=fallbackVideoTitle(u);
        $('#previewHost').textContent=u.hostname.replace(/^www\./,'');
        $('#previewProvider').textContent='KALİTE BİLGİSİ ALINAMADI';
      }
    }
  },250);
}

function planLabel(code){ return code==='PRO_5M'?'5 Aylık Pro':code==='LIFETIME'?'Sınırsız Pro':'1 Aylık Pro'; }

async function showProModal() {
  let payment = { iban: '', accountName: '', note: '' };
  try { const r = await fetch('/api/payment-info'); if (r.ok) payment = await r.json(); } catch {}
  showModal(`<span class="section-kicker">VIOLET PRO</span><h2>Kaliteni yükselt.</h2><p>Paket seç, ödemeyi IBAN'a gönder, ardından iletişim bilgini ve mesajını bırak. Sana Pro anahtar iletilecek.</p>
  <div class="plan-choices">
    <label class="plan-choice"><input type="radio" name="plan" value="PRO_1M"><span><b>1 Aylık Pro</b><small>30 gün</small></span><strong>₺50</strong></label>
    <label class="plan-choice"><input type="radio" name="plan" value="PRO_5M" checked><span><b>5 Aylık Pro</b><small>150 gün</small></span><strong>₺250</strong></label>
    <label class="plan-choice"><input type="radio" name="plan" value="LIFETIME"><span><b>Sınırsız Pro</b><small>Ömür boyu</small></span><strong>₺900</strong></label>
  </div>
  <div class="reward-card" style="margin:10px 0"><div><b>Ödeme (IBAN)</b></div><div style="margin-top:6px"><code id="ibanText">${escapeHtml(payment.iban || 'IBAN ayarlanmalı (appsettings.json)')}</code> <button class="copy-key" id="copyIban" data-code="${escapeHtml(payment.iban||'')}">Kopyala</button></div><small>${escapeHtml(payment.accountName||'')}</small><div style="margin-top:6px;color:#cfd0e6;font-size:12px">${escapeHtml(payment.note||'')}</div></div>
  <label class="admin-login-label">İletişim bilgisi<input id="contactInfo" class="modal-input" placeholder="Instagram / Telegram / E-posta / Discord"></label>
  <label class="admin-login-label">Mesaj<textarea id="purchaseMsg" class="modal-input" rows="3" style="min-height:84px;resize:vertical">Merhaba, 5 Aylık Pro almak istiyorum. Ödemeyi yaptım.</textarea></label>
  <div class="modal-buttons"><button class="modal-secondary" id="redeemKey">Pro anahtarım var</button><button class="modal-primary" id="sendPurchaseMsg">Mesaj gönder</button></div>
  <p class="modal-note">IBAN'ı <code>appsettings(.Development).json</code> içindeki <code>Payment:Iban</code> ve <code>Payment:AccountName</code> ile güncelleyin.</p>`);
  $$('input[name="plan"]').forEach(r=>r.addEventListener('change',()=>{ const v=$('input[name="plan"]:checked')?.value||'PRO_5M'; $('#purchaseMsg').value = `Merhaba, ${planLabel(v)} almak istiyorum. Ödemeyi yaptım.`; }));
  $('#copyIban')?.addEventListener('click', ()=>{ const v=$('#copyIban').dataset.code||''; if(!v) return toast('IBAN ayarlı değil.'); navigator.clipboard?.writeText(v); toast('IBAN kopyalandı.'); });
  $('#redeemKey').onclick = keyModal;
  $('#sendPurchaseMsg').onclick = async () => {
    const contact = $('#contactInfo').value.trim(); const pkg = $('input[name="plan"]:checked')?.value; const message = $('#purchaseMsg').value.trim();
    try {
      const r = await fetch('/api/messages', { method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ contact, package: pkg, message }) });
      const data = await r.json(); if(!r.ok) throw new Error(data.error || 'Mesaj gönderilemedi.');
      toast('Mesaj iletildi. Admin panelinden dönüş yapılacak.'); closeModal();
    } catch(e){ if(String(e.message||'').toLowerCase().includes('giriş')) accountModal(); else toast(e.message.includes('fetch')?'Sunucu/SQL bağlantısını kontrol edin.':e.message); }
  };
}

function keyModal() {
  showModal(`<span class="section-kicker">LİSANS AKTİVASYONU</span><h2>Pro anahtarını gir.</h2><p>Kod hesabına ve bu cihaza bağlanır.</p><div class="modal-form"><input id="keyEntry" placeholder="VIOLET-XXXX-XXXX-XXXX-XXXX"><button class="modal-primary" id="activateKey">Etkinleştir</button></div>`);
  $('#activateKey').onclick = async () => {
    const key = ($('#keyEntry').value||'').trim().toUpperCase(); if(!key) return toast('Aktivasyon kodunu gir.');
    try { const r = await fetch('/api/account/session',{credentials:'same-origin'}); const data = await r.json(); if(!data.authenticated && !data.admin){ state.pendingLicense=key; activationAccountPrompt(); return; } await redeemLicense(key); }
    catch(e){ toast(e.message || 'Hata oluştu.'); }
  };
}
function activationAccountPrompt(){ showModal(`<span class="section-kicker">LİSANS AKTİVASYONU</span><h2>Kodu hesabına bağla.</h2><p>Önce giriş yap veya hesap oluştur.</p><div class="modal-buttons"><button id="licenseLogin" class="modal-primary">Giriş yap</button><button id="licenseRegister" class="modal-secondary">Hesap oluştur</button></div>`); $('#licenseLogin').onclick=()=>renderAccountForm('login'); $('#licenseRegister').onclick=()=>renderAccountForm('register'); }
async function redeemLicense(code){
  try{
    let deviceId = localStorage.getItem('violet_device_id'); if(!deviceId){ deviceId = (crypto.randomUUID ? crypto.randomUUID() : String(Date.now())+Math.random()); localStorage.setItem('violet_device_id', deviceId); }
    const r = await fetch('/api/license/redeem',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify({code, deviceId})});
    const data = await r.json(); if(!r.ok) throw new Error(data.error||'Kod doğrulanamadı.');
    state.pendingLicense=''; await syncEntitlements(); closeModal(); toast(data.lifetime ? 'Sınırsız Pro hesabına eklendi.' : `${planLabel(data.planCode||'PRO_1M')} hesabına eklendi.`); return true;
  } catch(e){ state.pendingLicense=''; toast(e.message.includes('fetch')?'SQL bağlantısını kontrol edin.':e.message); return false; }
}

async function accountModal() {
  let account = null;
  try { const r = await fetch('/api/account/session', {credentials:'same-origin'}); if (r.ok) { const d = await r.json(); if (d.authenticated) account = d.user; } } catch {}
  if (account) { renderAccountModal(account); return; }
  renderAccountForm('login');
}
function renderAccountForm(mode) {
  const registering = mode === 'register';
  showModal(`<span class="section-kicker">VIOLET HESABI</span><h2>${registering ? 'Hesabını oluştur.' : 'Tekrar hoş geldin.'}</h2><p>E-posta/kullanıcı adı ve şifrenle giriş yap. Veriler SQL Server'da saklanır.</p>${registering ? '<label class="admin-login-label">Ad / Kullanıcı adı<input id="accountName" class="modal-input" autocomplete="name" placeholder="Ad veya kullanıcı adı"></label>' : ''}<label class="admin-login-label">E-posta veya kullanıcı adı<input id="accountEmail" class="modal-input" autocomplete="username" placeholder="ornek@mail.com veya kullanıcı adı"></label><label class="admin-login-label">Şifre<input id="accountPassword" class="modal-input" type="password" autocomplete="${registering?'new-password':'current-password'}" placeholder="${registering?'En az 10 karakter':'Şifren'}"></label><div id="accountError" class="admin-error"></div><div class="modal-buttons"><button id="accountSubmit" class="modal-primary">${registering?'Hesap oluştur':'Giriş yap'}</button><button id="accountSwitch" class="modal-secondary">${registering?'Giriş yap':'Yeni hesap'}</button></div>`);
  $('#accountSubmit').onclick = async () => {
    const button = $('#accountSubmit'); button.disabled = true;
    try {
      const payload = registering ? {displayName:$('#accountName').value,email:$('#accountEmail').value,password:$('#accountPassword').value} : {username:$('#accountEmail').value,password:$('#accountPassword').value};
      const r = await fetch(`/api/account/${registering?'register':'login'}`,{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
      const data = await r.json(); if (!r.ok) throw new Error(data.error || 'İşlem tamamlanamadı.');
      await syncEntitlements();
      if(state.pendingLicense){ const code=state.pendingLicense; const activated=await redeemLicense(code); if(!activated) renderAccountModal(data.user||state.user); }
      else { renderAccountModal(data.user||state.user); toast(registering?'Hesap oluşturuldu.':'Giriş yapıldı.'); }
    } catch (e) { $('#accountError').textContent = e.message.includes('fetch') ? 'SQL bağlantısı yok. SQLEXPRESS çalıştırın.' : e.message; button.disabled = false; }
  };
  $('#accountSwitch').onclick = () => renderAccountForm(registering?'login':'register');
  $('#accountPassword').addEventListener('keydown', e => { if(e.key==='Enter') $('#accountSubmit').click(); });
}
async function renderAccountModal(account) {
  let orders=[]; try { const r=await fetch('/api/orders',{credentials:'same-origin'}); if(r.ok) orders=(await r.json()).orders||[]; } catch {}
  const orderRows=orders.length?orders.map(o=>`<div class="account-order"><span>${planLabel(o.planCode||'PRO_1M')}</span><b>₺${o.amount}</b><small>${o.status==='PendingPayment'?'Ödeme bekliyor':escapeHtml(o.status)} · ${new Date(o.createdAt).toLocaleDateString('tr-TR')}</small></div>`).join(''):'<div class="admin-output">Henüz sipariş yok.</div>';
  const proText = account?.isPro ? (account.proLifetime ? 'Sınırsız Pro aktif.' : `Pro bitiş: ${account.proExpiresAt ? new Date(account.proExpiresAt).toLocaleDateString('tr-TR') : '-'}`) : 'Standart plan · 720p ve altı';
  showModal(`<span class="section-kicker">VIOLET HESABI</span><h2>Merhaba, ${escapeHtml(account?.displayName||'') }.</h2><p>${escapeHtml(account?.email||'')}</p><div class="account-pro-status">${proText}</div><div class="account-orders"><b>Sipariş geçmişi</b>${orderRows}</div><div class="modal-buttons"><button id="accountLogout" class="modal-secondary">Çıkış yap</button><button id="accountDone" class="modal-primary">Tamam</button></div>`);
  $('#accountDone').onclick=closeModal; $('#accountLogout').onclick=async()=>{ await fetch('/api/account/logout',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:'{}'}); await syncEntitlements(); closeModal(); toast('Çıkış yapıldı.'); };
}

function adminModal(notice = '') {
  if (notice && typeof notice !== 'string') notice = '';
  if (state.isAdmin) { adminDashboard(); return; }
  showModal(`<span class="section-kicker">YONETICI PANELI</span><h2>Admin girisi</h2><p>Yonetici hesabinizla giris yapin.</p>${notice ? `<div class="admin-error" style="margin-bottom:10px">${escapeHtml(notice)}</div>` : ''}<label class="admin-login-label">Kullanici adi<input id="adminUsername" class="modal-input" autocomplete="username" placeholder="Kullanici adi"></label><label class="admin-login-label">Parola<input id="adminPassword" class="modal-input" type="password" autocomplete="current-password" placeholder="Parola"></label><div id="adminLoginError" class="admin-error"></div><div class="modal-buttons"><button id="adminSignIn" class="modal-primary">Giris yap</button><button id="adminCancel" class="modal-secondary">Vazgec</button></div>`);
  document.querySelector('#adminCancel').onclick = closeModal;
  document.querySelector('#adminSignIn').onclick = async () => {
    const button = document.querySelector('#adminSignIn'); const err = document.querySelector('#adminLoginError'); if (err) err.textContent = '';
    button.disabled = true; button.textContent = 'Kontrol ediliyor...';
    try {
      const r = await fetch('/api/admin/login', { method:'POST', credentials:'same-origin', headers:{ 'Content-Type':'application/json' }, body:JSON.stringify({ username: (document.querySelector('#adminUsername')?.value || '').trim(), password: document.querySelector('#adminPassword')?.value || '' }) });
      const data = await r.json().catch(() => ({}));
      if (!r.ok) throw new Error(data.error || 'Giris basarisiz.');
      state.adminToken = data.token || '';
      state.isAdmin = true;
      updatePlan();
      await syncEntitlements();
      state.isAdmin = true;
      if (data.token) state.adminToken = data.token;
      await adminDashboard();
    }
    catch (e) { if (err) err.textContent = (e.message || '').includes('fetch') ? 'Sunucuya ulasilamiyor.' : (e.message || 'Hata'); button.disabled = false; button.textContent = 'Giris yap'; }
  };
  document.querySelector('#adminPassword')?.addEventListener('keydown', e => { if (e.key === 'Enter') document.querySelector('#adminSignIn').click(); });
}

async function adminDashboard() {
  try {
    const [kRes, sRes, mRes, uRes] = await Promise.all([
      fetch('/api/admin/keys', { credentials:'same-origin', headers: adminHeaders() }),
      fetch('/api/admin/stats', { credentials:'same-origin', headers: adminHeaders() }),
      fetch('/api/admin/messages', { credentials:'same-origin', headers: adminHeaders() }),
      fetch('/api/admin/users', { credentials:'same-origin', headers: adminHeaders() })
    ]);
    if (!kRes.ok) {
      let em = 'Oturum gerekli';
      try { const ej = await kRes.json(); if (ej && ej.error) em = ej.error; } catch {}
      if (kRes.status === 401 || kRes.status === 403 || /oturum|gerek|yetki|admin|y[oö]netici/i.test(em)) {
        state.isAdmin = false;
        state.adminToken = '';
        updatePlan();
        adminModal('Oturum doğrulanamadı. Lütfen admin girişi yapın.');
      }
      throw new Error(em);
    }
    const keys = (await kRes.json()).keys || [];
    const stats = sRes.ok ? await sRes.json() : {};
    const messages = mRes.ok ? ((await mRes.json()).messages||[]) : [];
    const users = uRes.ok ? ((await uRes.json()).users||[]) : [];
    showModal(`<span class="section-kicker">YÖNETİCİ PANELİ</span><h2>Yönetim</h2>
      <div class="admin-stats">${[['Kullanıcı',stats.totalUsers],['Aktif Pro',stats.activePro],['Toplam Key',stats.totalKeys],['Kullanılmamış',stats.unusedKeys],['Yeni Mesaj',stats.newMessages]].map(([l,v])=>`<div class="admin-stat"><small>${l}</small><b>${v??'-'}</b></div>`).join('')}</div>
      <div class="admin-tabs"><button class="selected" data-admin-tab="keys">Key</button><button data-admin-tab="messages">Mesajlar</button><button data-admin-tab="users">Kullanıcılar</button></div>
      <div id="adminPanel"></div>
      <div class="modal-buttons"><button id="adminLogout" class="modal-secondary">Çıkış yap</button></div>`);
    const renderTab = (tab) => {
      $$('.admin-tabs button').forEach(t => t.classList.toggle('selected', t.dataset.adminTab===tab));
      if(tab==='keys') $('#adminPanel').innerHTML = `<p>1/5 aylık veya sınırsız Pro anahtarı üret.</p><div class="modal-admin-grid"><label>Paket<select id="adminPlan"><option value="PRO_1M">1 Aylık (₺50)</option><option value="PRO_5M">5 Aylık (₺250)</option><option value="LIFETIME">Sınırsız (₺900)</option></select></label><label>Adet<input id="adminQuantity" type="number" min="1" max="20" value="1"></label></div><label class="admin-login-label">Not<input id="adminNote" class="modal-input" placeholder="Opsiyonel"></label><div class="modal-buttons"><button id="generateKey" class="modal-primary">Anahtar üret</button></div><div id="adminResult">${adminKeysHtml(keys)}</div>`;
      if(tab==='messages') $('#adminPanel').innerHTML = messages.length ? messages.map(m=>`<div class="admin-row"><b>${escapeHtml(m.name||m.email||'')}</b><span>${escapeHtml(m.email||'')} · ${escapeHtml(m.contact||'')} · ${escapeHtml(planLabel(m.package||''))}</span><small>${escapeHtml(m.status||'')} · ${new Date(m.createdAt).toLocaleDateString('tr-TR')}</small><div style="margin-top:6px;color:#cfd0e6">${escapeHtml(m.message||'')}</div></div>`).join('') : '<div class="admin-output">Henüz mesaj yok.</div>';
      if(tab==='users') $('#adminPanel').innerHTML = users.length ? users.map(u=>`<div class="admin-row"><b>${escapeHtml(u.displayName||'-')}</b><span>${escapeHtml(u.email||'')}</span><small>${u.role} · ${u.status} · ${u.proLifetime?'Sınırsız Pro':(u.proExpiresAt?('Pro bitiş '+new Date(u.proExpiresAt).toLocaleDateString('tr-TR')):'Ücretsiz')} · ${new Date(u.createdAt).toLocaleDateString('tr-TR')}</small></div>`).join('') : '<div class="admin-output">Henüz kullanıcı yok.</div>';
      if(tab==='keys') $('#generateKey').onclick = async () => {
        const button = $('#generateKey'); button.disabled = true;
        try {
          const response = await fetch('/api/admin/keys', { method:'POST', credentials:'same-origin', headers: adminHeaders({ 'Content-Type':'application/json' }), body:JSON.stringify({ planCode: $('#adminPlan').value, quantity: Number($('#adminQuantity').value), note: $('#adminNote').value || null }) });
          const result = await response.json(); if (!response.ok) throw new Error(result.error || 'Anahtar üretilemedi.');
          $('#adminResult').innerHTML = adminKeysHtml(result.keys, true) + ($('#adminResult').innerHTML||'');
          toast('Anahtar oluşturuldu.');
        } catch (e) { toast(e.message); } finally { button.disabled = false; }
      };
    };
    $$('.admin-tabs button').forEach(tab => tab.onclick = () => renderTab(tab.dataset.adminTab));
    renderTab('keys');
    $('#adminLogout').onclick = async () => { try { await fetch('/api/admin/logout', { method:'POST', credentials:'same-origin', headers: adminHeaders({ 'Content-Type':'application/json' }), body:'{}' }); } catch {} state.adminToken=''; state.isAdmin=false; await syncEntitlements(); closeModal(); };
  } catch (e) {
    const msg = (e && e.message) ? e.message : 'Panel yuklenemedi.';
    if (msg.includes('fetch') || msg.includes('Network')) { toast('Sunucuya ulasilamiyor.'); return; }
    showModal(`<span class="section-kicker">YONETICI PANELI</span><h2>Panel acilamadi</h2><p>${escapeHtml(msg)}</p><div class="modal-buttons"><button id="adminRetry" class="modal-primary">Tekrar dene</button><button id="adminForceLogout" class="modal-secondary">Cikis yap</button><button id="adminCloseErr" class="modal-secondary">Kapat</button></div>`);
    $('#adminRetry').onclick = () => adminDashboard();
    $('#adminCloseErr').onclick = closeModal;
    $('#adminForceLogout').onclick = async () => { try { await fetch('/api/admin/logout', { method:'POST', credentials:'same-origin', headers: adminHeaders({ 'Content-Type':'application/json' }), body:'{}' }); } catch {} state.adminToken=''; state.isAdmin=false; await syncEntitlements(); closeModal(); };
  }
}
function adminKeysHtml(keys, fresh = false) {
  if (!keys?.length) return '<div class="admin-output">Henüz anahtar yok.</div>';
  return keys.map(k => `<div class="key-result${fresh ? ' fresh-key' : ''}"><code>${escapeHtml(k.code)}</code> · ${escapeHtml(planLabel(k.planCode||''))} · ${escapeHtml(k.status||'')} <small>${new Date(k.createdAt).toLocaleDateString('tr-TR')}</small> <button class="copy-key" data-code="${escapeHtml(k.code)}">Kopyala</button></div>`).join('');
}

async function rewardsModal() {
  let status = null;
  try {
    const r = await fetch('/api/ads/status', { credentials: 'same-origin' });
    status = await r.json();
    if (!r.ok) throw new Error(status.error || 'Durum alınamadı');
  } catch (e) {
    if (String(e.message||'').includes('fetch') || String(e.message||'').includes('Network')) return toast('Sunucuya ulaşılamıyor.');
    if (String(e.message||'').toLowerCase().includes('giriş')) { accountModal(); return; }
    return toast(e.message);
  }
  state.reward = status.completedTasks || 0;
  updatePlan();
  const configured = !!status.configured;
  const credits = status.availableCredits || 0;
  const req = status.requiredTasks || 4;
  const hours = status.rewardHours || 24;
  showModal(`<span class="section-kicker">ÖDÜLLÜ REKLAM</span><h2>${credits>0 ? 'Ödülün hazır!' : 'Reklam izle, Pro kazan.'}</h2>
    <p>${escapeHtml(status.note || '')}</p>
    <div class="reward-card"><div class="reward-count"><span id="rewardNow">${status.completedTasks||0}</span> / ${req}</div><small>doğrulanmış görev · ${credits} kredi (her kredi ${hours} saat Pro)</small></div>
    <div class="modal-buttons">
      <button id="closeReward" class="modal-secondary">Kapat</button>
      ${credits>0 ? `<button id="redeemReward" class="modal-primary">Krediyi kullan (${hours} saat Pro)</button>` : `<button id="openAdTask" class="modal-primary" ${configured?'':'disabled'}>Reklamı aç</button>`}
    </div>
    ${configured ? `<p class="modal-note">Postback doğrulandıktan sonra bu ekranı yenileyip kredini kullanabilirsin. Adsterra postback URL'ine <code>userId</code> ve <code>secret</code> parametrelerini eklemelisin.</p>` : `<p class="modal-note">Yapılandırma için: <code>appsettings(.Development).json</code> içinde Ads:Enabled=true, Ads:AdUrl ve Ads:PostbackSecret ayarla. Adsterra postback örneği: <code>/api/ads/postback?secret=...&userId=&lt;KULLANICI_ID&gt;</code></p>`}
    ${configured ? `<div class="admin-output" style="margin-top:8px;font-size:12px">Senin kullanıcı ID: <code>${escapeHtml(status.userId || (state.user?.id||''))}</code></div>` : ''}`);
  $('#closeReward').onclick = closeModal;
  $('#openAdTask') && ($('#openAdTask').onclick = async () => {
    try {
      const sr = await fetch('/api/ads/start', { method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json'}, body:'{}' });
      const sd = await sr.json();
      if (!sr.ok) throw new Error(sd.error || 'Reklam açılamadı');
      const url = sd.adUrl || status.adUrl;
      if (!url) throw new Error('Reklam URL bulunamadı');
      window.open(url, '_blank', 'noopener');
      toast('Reklam yeni sekmede açıldı. Tamamlama Adsterra postback ile doğrulanınca kredi artar.');
    } catch (e) { toast(e.message); }
  });
  $('#redeemReward') && ($('#redeemReward').onclick = async () => {
    try {
      const rr = await fetch('/api/ads/redeem', { method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json'}, body:'{}' });
      const rd = await rr.json();
      if (!rr.ok) throw new Error(rd.error || 'Kredi kullanılamadı');
      await syncEntitlements();
      toast(`${rd.rewardHours || hours} saat Pro aktif edildi.`);
      rewardsModal();
    } catch (e) { toast(e.message); }
  });
}
async function loadLibrary(){
  try {
    const r = await fetch('/api/library', {credentials:'same-origin'});
    if (r.ok) { const d = await r.json(); state.downloads = (d.items||[]).map(x=>({ id:x.id, title:x.videoTitle, host:x.platform, quality:x.resolution, mode:(x.format||'').toLowerCase()==='mp3'?'audio':'video', date:x.createdAt })); }
  } catch {}
  renderLibrary();
}
function renderLibrary() {
  $('#libraryCount').textContent = state.downloads.length;
  $('#libraryEmpty').classList.toggle('hidden', state.downloads.length > 0);
  $('#libraryItems').innerHTML = state.downloads.map(d => `<div class="library-row"><div class="preview-thumb">${d.mode === 'audio' ? '♪' : '▶'}</div><div><b>${escapeHtml(d.title||'')}</b><small>${escapeHtml(d.host||'')} · ${escapeHtml(d.quality||'')}${d.date ? ' · '+new Date(d.date).toLocaleDateString('tr-TR') : ''}</small></div><span class="standard-badge">${d.id ? 'KAYIT' : 'OTURUM'}</span></div>`).join('');
}
function navigate(tab) { const isLibrary = tab === 'library'; $('#studioView').classList.toggle('active-view', !isLibrary); $('#libraryView').classList.toggle('active-view', isLibrary); $$('.nav-item').forEach(n => n.classList.toggle('active', n.dataset.tab === tab)); $('#breadcrumbPage').textContent = isLibrary ? 'Kütüphanem' : 'İndirici'; if(isLibrary) loadLibrary(); }

function formatBytes(n){ if(!n || n<0) return ''; const u=['B','KB','MB','GB']; let i=0,x=n; while(x>=1024&&i<u.length-1){x/=1024;i++;} return (i===0?x:x.toFixed(x>=100?0:1))+' '+u[i]; }
function showDownloadBar(title, path, percentText, percentVal){
  const bar=$('#downloadBar'); if(!bar) return;
  bar.classList.remove('hidden','done','error');
  $('#downloadBarTitle').textContent = title || 'İndirme';
  $('#downloadBarPath').textContent = path || '';
  if(percentText!=null) $('#downloadBarPercent').textContent = percentText;
  if(typeof percentVal==='number'){
    const ind = percentVal < 0;
    $('#downloadBarFill').style.width = (ind ? 100 : Math.max(0, Math.min(100, percentVal))) + '%';
    $('#downloadBarFill').style.opacity = ind ? '0.4' : '1';
  }
}
function setDownloadBarDone(title, path){ const bar=$('#downloadBar'); if(!bar) return; bar.classList.remove('hidden','error'); bar.classList.add('done'); $('#downloadBarTitle').textContent=title||'İndirme tamamlandı'; if(path!=null) $('#downloadBarPath').textContent=path; $('#downloadBarPercent').textContent='100%'; $('#downloadBarFill').style.width='100%'; $('#downloadBarFill').style.opacity='1'; }
function setDownloadBarError(title, detail){ const bar=$('#downloadBar'); if(!bar) return; bar.classList.remove('hidden','done'); bar.classList.add('error'); $('#downloadBarTitle').textContent=title||'İndirme başarısız'; if(detail!=null) $('#downloadBarPath').textContent=detail; $('#downloadBarPercent').textContent='Hata'; $('#downloadBarFill').style.width='100%'; $('#downloadBarFill').style.opacity='0.6'; }
$('#downloadBarClose')?.addEventListener('click', ()=> $('#downloadBar')?.classList.add('hidden'));

try {
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', ev => {
      try {
        let msg = ev.data || {};
        if (typeof msg === 'string') { try { msg = JSON.parse(msg); } catch { msg = {}; } }
        if (msg.type === 'download-started') {
          showDownloadBar('İndiriliyor: ' + (msg.file || ''), msg.path || 'Kaydedilecek konum seçildi', '0%', 0);
        } else if (msg.type === 'download-progress') {
          const p = (typeof msg.percent === 'number') ? msg.percent : -1;
          const txt = p >= 0 ? (Math.floor(p) + '%') : (msg.received ? formatBytes(msg.received) : 'İndiriliyor');
          showDownloadBar('İndiriliyor: ' + (msg.file || ''), msg.path || '', txt, p >= 0 ? p : -1);
        } else if (msg.type === 'download-completed') {
          setDownloadBarDone('İndirme tamamlandı: ' + (msg.file || ''), msg.path || '');
          toast('İndirme tamamlandı.');
        } else if (msg.type === 'download-interrupted') {
          setDownloadBarError('İndirme durduruldu', (msg.reason ? (msg.reason + ' — ') : '') + (msg.path || ''));
        } else if (msg.type === 'download-cancelled') {
          $('#downloadBar')?.classList.add('hidden');
          toast('İndirme iptal edildi.');
        }
      } catch {}
    });
  }
} catch {}

$('#urlInput').addEventListener('input', inspectLink); $('#urlInput').addEventListener('paste', () => setTimeout(inspectLink, 0));
$('#pasteButton').onclick = async () => { try { $('#urlInput').value = await navigator.clipboard.readText(); inspectLink(); } catch { $('#urlInput').focus(); toast('Linki yapıştırın.'); } };
$('#clearLink').onclick = () => { $('#urlInput').value = ''; inspectLink(); };
$('#videoMode').onclick = () => { state.mode = 'video'; $('#videoMode').classList.add('selected'); $('#audioMode').classList.remove('selected'); $('#qualityOptions').classList.remove('hidden'); $('#audioOptions').classList.add('hidden'); };
$('#audioMode').onclick = () => { state.mode = 'audio'; $('#audioMode').classList.add('selected'); $('#videoMode').classList.remove('selected'); $('#qualityOptions').classList.add('hidden'); $('#audioOptions').classList.remove('hidden'); };
$('#qualityOptions').addEventListener('click', event => { const card=event.target.closest('.quality-card'); if(!card) return; if(card.classList.contains('locked') && !state.pro && !state.isAdmin){ showProModal(); return; } setQuality(card.dataset.quality, card.dataset.height || ({'4K':2160,'2K':1440,'1080p':1080,'720p':720,'480p':480,'360p':360}[card.dataset.quality])); });
$('#downloadButton').onclick = async () => {   const u = parseLink($('#urlInput').value), msg = $('#statusMessage'), button = $('#downloadButton');   msg.classList.remove('hidden');   if (!u) { msg.textContent = 'Once gecerli bir video baglantisi ekleyin.'; return; }   if (state.mode === 'video' && !state.availableQualities?.some(q => Number(q.height) === state.height)) { msg.textContent = 'Kalite bilgisi cozumlenemedi, linki tekrar yapistirin.'; return; }   if (state.mode === 'video' && state.height > 720 && !state.pro && !state.isAdmin) { showProModal(); return; }   button.disabled = true;   msg.textContent = 'Video hazirlaniyor (sunucuda isleniyor)...';   const dlTitle = state.resolvingTitle || $('#previewTitle')?.textContent || 'Video';   showDownloadBar('Hazirlaniyor: ' + dlTitle, 'Sunucuda isleniyor, bitince kayit konumunu secebilirsiniz', 'Hazirlaniyor', -1);   try {     const r = await fetch('/api/video/download', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ url: u.href, mode: state.mode, height: state.height, title: state.resolvingTitle || $('#previewTitle')?.textContent || '', thumbnail: state.resolvingThumb || null, platform: state.resolvingProvider || null }) });     const ct = (r.headers.get('content-type') || '').toLowerCase();     if (!r.ok) {       let err = 'Indirme basarisiz.';       if (ct.includes('application/json')) { const data = await r.json().catch(() => ({})); err = data.error || err; }       else { const t = await r.text().catch(() => ''); if (t) err = t.slice(0, 300); }       throw new Error(err);     }     if (ct.includes('application/json')) {       const data = await r.json().catch(() => ({}));       msg.textContent = data.message || 'Islem tamamlandi.';       toast('Tamamlandi.');       setDownloadBarDone('Tamamlandi', '');     } else {       let filename = (state.resolvingTitle || 'video').replace(/[\\/:*?"<>|]+/g, '_').slice(0, 120) + (state.mode === 'audio' ? '.mp3' : '.mp4');       const cd = r.headers.get('content-disposition') || '';       const m = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(cd);       if (m) { try { filename = decodeURIComponent(m[1] || m[2]); } catch { filename = m[1] || m[2] || filename; } }       const total = Number(r.headers.get('content-length') || 0);       let blob;       if (r.body && r.body.getReader) {         const reader = r.body.getReader();         const chunks = [];         let received = 0;         while (true) {           const { done, value } = await reader.read();           if (done) break;           if (value) { chunks.push(value); received += value.length; }           if (total > 0) {             const pct = Math.max(0, Math.min(100, (received / total) * 100));             showDownloadBar('Indiriliyor: ' + filename, 'Sunucudan aliniyor (' + formatBytes(received) + ' / ' + formatBytes(total) + ')', Math.floor(pct) + '%', pct);           } else {             showDownloadBar('Indiriliyor: ' + filename, 'Sunucudan aliniyor (' + formatBytes(received) + ')', formatBytes(received), -1);           }         }         blob = new Blob(chunks, { type: ct || (state.mode === 'audio' ? 'audio/mpeg' : 'video/mp4') });       } else {         showDownloadBar('Indiriliyor: ' + filename, 'Sunucudan aliniyor...', 'Indiriliyor', -1);         blob = await r.blob();       }       showDownloadBar('Kaydet: ' + filename, window.chrome?.webview ? 'Kayit konumunu secin...' : 'Indirme baslatiliyor...', '100%', 100);       const obj = URL.createObjectURL(blob);       const a = document.createElement('a'); a.href = obj; a.download = filename; document.body.appendChild(a); a.click(); a.remove();       setTimeout(() => URL.revokeObjectURL(obj), 60000);       msg.textContent = window.chrome?.webview ? 'Kayit konumunu secin. Ilerleme altta gosterilecek.' : 'Indirme baslatildi.';       toast(window.chrome?.webview ? 'Kayit konumunu secin.' : 'Indirme basladi.');       if (!window.chrome?.webview) setTimeout(() => setDownloadBarDone('Indirme baslatildi: ' + filename, ''), 800);     }     state.downloads.unshift({ title: state.resolvingTitle || $('#previewTitle')?.textContent || 'Video', host: state.resolvingProvider || u.hostname, quality: state.mode === 'audio' ? 'MP3' : state.quality, mode: state.mode });     renderLibrary();   } catch (e) { msg.textContent = e.message; toast(e.message); setDownloadBarError('Indirme basarisiz', e.message || ''); } finally { button.disabled = false; } }
$('#upgradeButton').onclick = () => showProModal(); $('#seePlans').onclick = () => showProModal(); $('#adminButton').onclick = () => adminModal(); $('#watchAdsButton').onclick = () => rewardsModal(); $('#profileButton').onclick = () => accountModal();
$$('.nav-item').forEach(n => n.onclick = () => navigate(n.dataset.tab)); $('#backToStudio').onclick = () => navigate('studio'); $('#emptyStart').onclick = () => navigate('studio');
$('#modalClose').onclick = closeModal; $('.modal-backdrop').onclick = closeModal; document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModal(); });
document.addEventListener('click', e => { const b = e.target.closest('.copy-key'); if(b){ navigator.clipboard?.writeText(b.dataset.code||''); toast('Kopyalandı.'); } });

async function syncEntitlements(){
  try {
    const [a,u] = await Promise.all([fetch('/api/admin/session',{credentials:'same-origin', headers: adminHeaders()}), fetch('/api/account/session',{credentials:'same-origin'})]);
    const admin = a.ok ? await a.json() : null; const account = u.ok ? await u.json() : null;
    state.isAdmin = !!admin?.authenticated;
    if (admin?.token) state.adminToken = admin.token;
    if (!state.isAdmin) state.adminToken = '';
    state.user = account?.user || null;
    state.pro = state.isAdmin || !!state.user?.isPro;
    updatePlan();
    if (state.user) loadLibrary();
  } catch {}
}
updatePlan(); renderLibrary(); syncEntitlements();
fetch('/api/status').then(r=>r.ok?r.json():null).then(data=>{ if(data && !data.database) toast('SQL Server bağlantısı yok. SQLEXPRESS hizmetini başlatın.'); }).catch(()=>{});
