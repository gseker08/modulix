// Yatay kayan sekme şeritleri (.record-tabs, .side-tabs): taşan uçta soluklaşma ipucu için sınıf ekler ve etkin sekmeyi görünür alana
// getirir (docs/design-system/component-catalog.md → "Kayan sekme şeritleri"). Bileşen koduna dokunmaz; Blazor çizimini DOM'dan izler.
(function () {
    const selector = '.record-tabs, .side-tabs';

    // Şerit taşıyorsa ve ucunda gizli içerik varsa ilgili uca soluklaşma sınıfı verilir (CSS: controls.css).
    function update(bar) {
        const hidden = bar.scrollWidth - bar.clientWidth;
        const overflow = hidden > 1;
        bar.classList.toggle('tabs-fade-start', overflow && bar.scrollLeft > 1);
        bar.classList.toggle('tabs-fade-end', overflow && bar.scrollLeft < hidden - 1);
    }

    // Etkin sekme tam görünmüyorsa şeridin ortasına kaydırılır; zaten görünüyorsa dokunulmaz.
    function reveal(bar) {
        if (bar.scrollWidth - bar.clientWidth <= 1) {
            return;
        }

        const active = bar.querySelector('[aria-selected="true"]');
        if (!active) {
            return;
        }

        const area = bar.getBoundingClientRect();
        const tab = active.getBoundingClientRect();
        if (tab.left >= area.left && tab.right <= area.right) {
            return;
        }

        bar.scrollLeft += (tab.left + tab.width / 2) - (area.left + area.width / 2);
    }

    function refresh(bars) {
        // Yerleşim oturduktan sonra ölçülür (yazı tipi ve sekme sayaçları genişliği değiştirir).
        requestAnimationFrame(() => bars.forEach(bar => {
            reveal(bar);
            update(bar);
        }));
    }

    new MutationObserver(records => {
        const bars = new Set();
        for (const record of records) {
            if (record.type === 'attributes') {
                const bar = record.target.closest && record.target.closest(selector);
                if (bar) {
                    bars.add(bar);
                }

                continue;
            }

            record.addedNodes.forEach(node => {
                if (node.nodeType !== 1) {
                    return;
                }

                if (node.matches(selector)) {
                    bars.add(node);
                }

                node.querySelectorAll(selector).forEach(bar => bars.add(bar));
            });
        }

        if (bars.size > 0) {
            refresh(bars);
        }
    }).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ['aria-selected'] });

    // Kaydırma olayı kabarmaz; yakalama aşamasında dinlenir.
    document.addEventListener('scroll', event => {
        if (event.target instanceof Element && event.target.matches(selector)) {
            update(event.target);
        }
    }, true);

    window.addEventListener('resize', () => refresh(document.querySelectorAll(selector)));
})();
