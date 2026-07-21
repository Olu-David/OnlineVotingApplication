// ===== VOTERS LAYOUT JAVASCRIPT =====

document.addEventListener('DOMContentLoaded', function () {
    // ===== NAVBAR ACTIVE LINK =====
    // Highlight current page in navbar
    const currentUrl = window.location.pathname;
    document.querySelectorAll('.navbar-menu a').forEach(link => {
        const href = link.getAttribute('href');
        if (currentUrl.includes(href)) {
            link.style.color = 'white';
            link.style.backgroundColor = 'rgba(255, 255, 255, 0.2)';
        }
    });

    // ===== SMOOTH SCROLLING =====
    // Smooth scroll for anchor links
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            const href = this.getAttribute('href');
            if (href !== '#') {
                e.preventDefault();
                const target = document.querySelector(href);
                if (target) {
                    target.scrollIntoView({ behavior: 'smooth' });
                }
            }
        });
    });

    // ===== ALERT AUTO-DISMISS =====
    // Auto dismiss alerts after 5 seconds
    const alerts = document.querySelectorAll('.alert');
    alerts.forEach(alert => {
        setTimeout(function () {
            alert.style.opacity = '0';
            alert.style.transition = 'opacity 0.3s ease';
            setTimeout(function () {
                alert.remove();
            }, 300);
        }, 5000);
    });

    console.log('✅ Voters layout loaded');
});