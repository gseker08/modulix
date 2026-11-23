// Uygulama diyaloğunun açıkken dış tıklama, Escape ve odak davranışları: Tab diyaloğun içinde döner,
// sayfa arkada kaymaz (html.app-launcher-open).
let cleanup = null;

function attach(dotNetRef, panel, button) {
    detach();

    const inside = target => panel.contains(target) || button.contains(target);
    // Odak sayfadaki başka bir denetime geçmediyse (gövdede ya da panelde kaldıysa) düğmeye dönülür.
    const focusIsFree = () => document.activeElement === document.body || panel.contains(document.activeElement);

    const onPointerDown = e => {
        if (!inside(e.target)) {
            dotNetRef.invokeMethodAsync('Dismiss', false);
        }
    };
    const onFocusIn = e => {
        if (!inside(e.target)) {
            dotNetRef.invokeMethodAsync('Dismiss', false);
        }
    };
    const focusable = () => Array.from(panel.querySelectorAll('a[href], button:not([disabled]), input:not([disabled])'))
        .filter(el => el.offsetParent !== null);
    const onKeyDown = e => {
        if (e.key === 'Escape') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('Dismiss', focusIsFree());
        } else if (e.key === 'Tab' && panel.contains(document.activeElement)) {
            const items = focusable();
            if (items.length === 0) {
                e.preventDefault();
                return;
            }

            const first = items[0];
            const last = items[items.length - 1];
            if (e.shiftKey && (document.activeElement === first || document.activeElement === panel)) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        }
    };

    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('focusin', onFocusIn);
    document.addEventListener('keydown', onKeyDown);
    document.documentElement.classList.add('app-launcher-open');
    cleanup = () => {
        document.documentElement.classList.remove('app-launcher-open');
        document.removeEventListener('pointerdown', onPointerDown, true);
        document.removeEventListener('focusin', onFocusIn);
        document.removeEventListener('keydown', onKeyDown);
    };
}

function detach() {
    if (cleanup) {
        cleanup();
        cleanup = null;
    }
}

// Sık kullanılanlar tarayıcıda saklanır; depolama kapalıysa seçim yalnızca açık panelde geçerli olur.
function readFavorites(key) {
    try {
        return window.localStorage.getItem(key);
    } catch {
        return null;
    }
}

function writeFavorites(key, value) {
    try {
        window.localStorage.setItem(key, value);
    } catch {
        // Depolama kapalı.
    }
}

window.modulixAppLauncher = { attach, detach, readFavorites, writeFavorites };
