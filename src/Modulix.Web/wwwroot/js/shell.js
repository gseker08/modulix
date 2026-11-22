// Uygulama kabuğu tercihleri: tema (açık/koyu) ve sol menünün daraltılmış durumu.
// Bu dosya <head> içinde, Blazor yüklenmeden önce çalışır; böylece sayfa yanlış temayla yanıp sönmez.
// Tercihler kullanıcının tarayıcısında saklanır (sunucuda tema politikası yoktur; eklenirse öncelik ondadır).
(function () {
    const themeKey = 'modulix.theme';
    const sidebarKey = 'modulix.sidebarCollapsed';
    const languageKey = 'modulix.language';
    const root = document.documentElement;

    function read(key) {
        try {
            return window.localStorage.getItem(key);
        } catch {
            return null;
        }
    }

    function write(key, value) {
        try {
            window.localStorage.setItem(key, value);
        } catch {
            // Depolama kapalıysa tercih yalnızca bu oturumda geçerli olur.
        }
    }

    function applyTheme(theme) {
        const value = theme === 'dark' ? 'dark' : 'light';
        root.setAttribute('data-theme', value);
        root.setAttribute('data-bs-theme', value);
        return value;
    }

    // Firma vurgu rengi (E8): yalnızca #RRGGBB kabul edilir (sunucu da doğrular); hover ve rgb türetilir, açık temada
    // koyulaştırılır, koyu temada açılır. Renk yoksa ya da geçersizse stil öğesi kaldırılır ve tokens.css geçerli olur.
    function accentTokens(hex, towards) {
        if (!/^#[0-9A-Fa-f]{6}$/.test(hex || '')) {
            return null;
        }

        const channels = [1, 3, 5].map(i => parseInt(hex.substr(i, 2), 16));
        const hover = channels.map(c => Math.round(c + (towards - c) * 0.12));
        return `--mx-brand-primary:${hex};--mx-brand-primary-rgb:${channels.join(', ')};`
            + `--mx-brand-primary-hover:rgb(${hover.join(', ')});`;
    }

    function applyAccent(light, dark) {
        const lightTokens = accentTokens(light, 0);
        const darkTokens = accentTokens(dark, 255);
        let style = document.getElementById('mx-accent-style');
        if (!lightTokens && !darkTokens) {
            style?.remove();
            return;
        }

        if (!style) {
            style = document.createElement('style');
            style.id = 'mx-accent-style';
            document.head.appendChild(style);
        }

        style.textContent = (lightTokens ? `:root,[data-theme="light"]{${lightTokens}}` : '')
            + (darkTokens ? `[data-theme="dark"]{${darkTokens}}` : '');
    }

    function applySidebar(collapsed) {
        root.classList.toggle('sidebar-collapsed', collapsed);
        return collapsed;
    }

    // Varsayılan tema açıktır; kullanıcı açıkça koyu seçmediyse açık tema kullanılır.
    applyTheme(read(themeKey));
    applySidebar(read(sidebarKey) === 'true');

    // Komut paleti kısayolu: Ctrl+K (macOS'ta ⌘K) genel aramaya odaklanır. Arama kutusu yoksa (giriş ekranı) tarayıcının kısayolu çalışır.
    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'k') {
            const input = document.getElementById('global-search-input');
            if (input) {
                event.preventDefault();
                input.focus();
                input.select();
            }
        }
    });

    window.modulixShell = {
        setAccent: (light, dark) => applyAccent(light, dark),
        getTheme: () => root.getAttribute('data-theme') || 'light',
        setTheme: theme => {
            const value = applyTheme(theme);
            write(themeKey, value);
            return value;
        },
        toggleTheme: () => window.modulixShell.setTheme(window.modulixShell.getTheme() === 'dark' ? 'light' : 'dark'),
        // Giriş öncesi ekranların dili: son seçilen dil, yoksa tarayıcının dili (ADR-0034). Oturum açınca sunucudaki tercih geçerli olur.
        getLanguage: () => read(languageKey),
        setLanguage: language => write(languageKey, language),
        browserLanguage: () => (navigator.languages && navigator.languages[0]) || navigator.language || '',
        isNarrow: () => window.matchMedia('(max-width: 767.98px)').matches,
        isSidebarCollapsed: () => root.classList.contains('sidebar-collapsed'),
        setSidebarCollapsed: collapsed => {
            write(sidebarKey, collapsed ? 'true' : 'false');
            return applySidebar(collapsed);
        }
    };
})();
