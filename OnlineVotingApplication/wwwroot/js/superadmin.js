// ===== SUPER ADMIN LAYOUT JAVASCRIPT =====

document.addEventListener('DOMContentLoaded', function () {
    // Get DOM elements
    const toggleBtn = document.getElementById('toggleSidebar');
    const sidebar = document.getElementById('sidebar');

    // ===== SIDEBAR TOGGLE =====
    // Toggle sidebar on mobile when hamburger is clicked
    if (toggleBtn && sidebar) {
        toggleBtn.addEventListener('click', function () {
            sidebar.classList.toggle('show');
        });

        // Close sidebar when a menu item is clicked (mobile only)
        document.querySelectorAll('.menu-item').forEach(item => {
            item.addEventListener('click', function () {
                if (window.innerWidth < 768) {
                    sidebar.classList.remove('show');
                }
            });
        });
    }

    // ===== ACTIVE MENU ITEM =====
    // Highlight the current page in sidebar
    const currentUrl = window.location.pathname;
    document.querySelectorAll('.menu-item').forEach(item => {
        const href = item.getAttribute('href');
        if (currentUrl.includes(href)) {
            item.style.backgroundColor = 'rgba(212, 175, 55, 0.2)';
            item.style.borderLeftColor = '#D4AF37';
        }
    });

    // ===== RESPONSIVE BEHAVIOR =====
    // Handle window resize
    window.addEventListener('resize', function () {
        if (window.innerWidth >= 768) {
            sidebar.classList.remove('show');
        }
    });

    console.log('✅ Super Admin layout loaded');

});