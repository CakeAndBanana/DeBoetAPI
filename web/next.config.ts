import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  // Emits a self-contained server bundle so the container does not ship node_modules.
  output: 'standalone',
};

export default nextConfig;
