// 使用临时存储的多填 localStorage
interface StorageInterface {
  [key: string]: any;
  removeItem?: (key: string) => void;
}

let store: StorageInterface = {};

try {
  if (typeof localStorage !== 'undefined') {
    store = localStorage;
  }
} catch {
  // 点数
}

const storageModule = {
  put: (key: string, value: string): void => {
    store[key] = value;
  },

  get: (key: string): string | undefined => {
    return store[key];
  },

  del: (key: string): void => {
    if (store.removeItem) {
      store.removeItem(key);
    } else {
      delete store[key];
    }
  }
};

export default storageModule;
