document.addEventListener('DOMContentLoaded', function () {
    const menu = document.getElementById('mainNavMenu');
    if (!menu) return;
    const links = menu.querySelectorAll('.nav-link');
    links.forEach(link => {
        link.addEventListener('click', () => {
            const bsCollapse = bootstrap.Collapse.getInstance(menu);
            if (bsCollapse) bsCollapse.hide();
        });
    });
});