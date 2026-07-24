document.addEventListener('DOMContentLoaded', function () {
    const sidebar = document.getElementById('adminSidebar');
    const overlay = document.getElementById('sidebarOverlay');
    const toggleBtn = document.getElementById('sidebarToggle');

    if (!sidebar || !toggleBtn) return;

    // Toggle button
    toggleBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        const isMobile = window.innerWidth < 992;

        if (isMobile) {
            sidebar.classList.toggle('mobile-open');
            overlay.classList.toggle('show');
        } else {
            sidebar.classList.toggle('collapsed');
        }
    });

    // Close overlay
    if (overlay) {
        overlay.addEventListener('click', function () {
            sidebar.classList.remove('mobile-open');
            overlay.classList.remove('show');
        });
    }

    // Close drawer when resizing to desktop
    window.addEventListener('resize', function () {
        if (window.innerWidth >= 992) {
            sidebar.classList.remove('mobile-open');
            if (overlay) overlay.classList.remove('show');
        }
    });

    // Persist collapsed state (desktop only)
    const saved = localStorage.getItem('sidebar-collapsed');
    if (saved === 'true' && window.innerWidth >= 992) {
        sidebar.classList.add('collapsed');
    }

    toggleBtn.addEventListener('click', function () {
        if (window.innerWidth >= 992) {
            const isCollapsed = sidebar.classList.contains('collapsed');
            localStorage.setItem('sidebar-collapsed', isCollapsed ? 'true' : 'false');
        }
    });

    // Close drawer when a link is clicked (mobile)
    const links = sidebar.querySelectorAll('.nav-link');
    links.forEach(link => {
        link.addEventListener('click', function () {
            if (window.innerWidth < 992) {
                sidebar.classList.remove('mobile-open');
                if (overlay) overlay.classList.remove('show');
            }
        });
    });
});