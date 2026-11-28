import React from 'react';

// 从 public/ 提供资源，使构建原样复制该资源，确保 bundle 与 source map 的确定性。
const logo = `${import.meta.env.BASE_URL}img/OrleansLogo.png`;

export default function BrandHeader() {
  return (
    <div className="brand-link">
      <a href="#" style={{ textDecoration: 'none' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <img src={logo} alt="Orleans Logo" className="brand-logo" />
          <h1 className="brand-title">
            Orleans Dashboard
          </h1>
        </div>
      </a>
      <div id="version-content" className="brand-version"></div>
    </div>
  );
}
