import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// base './' => funguje na GitHub Pages pod libovolným názvem repozitáře
export default defineConfig({ base: './', plugins: [react()] });
