import path from 'path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const srcDir = path.resolve(__dirname, 'src');

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      Components: path.join(srcDir, 'components'),
      components: path.join(srcDir, 'components'),
      Styles: path.join(srcDir, 'styles'),
      styles: path.join(srcDir, 'styles'),
      Activity: path.join(srcDir, 'Activity'),
      Settings: path.join(srcDir, 'Settings'),
      System: path.join(srcDir, 'System')
    }
  },
  css: {
    postcss: path.resolve(__dirname, 'postcss.config.cjs'),
    modules: {
      generateScopedName: '[name]_[local]_[hash:base64:5]'
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:8686',
        changeOrigin: true
      },
      '/signalr': {
        target: 'http://localhost:8686',
        ws: true,
        changeOrigin: true
      }
    }
  }
});
