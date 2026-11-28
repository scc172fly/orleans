// durable-operations.js：以幂等方式从浏览器启动并观察持久化 HTTP 操作。
//
// 每个用户意图（例如购物车结账）都有一个幂等键，辅助工具在发送请求之前将其持久化。
// 网络故障、双击或页面刷新后重试请求时，仍携带同一个键，
// 因此服务器会定位到首次尝试所启动的操作。一旦服务器确认启动，
// 辅助工具便删除该意图的键，并持久化操作的 Operation-Location，直到操作进入终态，
// 这样刷新后的页面可以继续观察该操作。
//
// 辅助工具使用 Orleans 持久化操作协议，该协议与 Azure 长时间运行操作兼容：
// 启动请求返回带结果的 200 OK，或返回 202 Accepted 或 424 Failed Dependency，并通过
// Operation-Location 提供状态监视器；监视器报告 { id, status, result, error }；{monitor}/events 以
// 服务器发送事件（SSE）的方式流式传输状态；GET {monitor} 携带 Prefer: wait=N 进行长轮询；POST {monitor}:cancel 取消操作；
// POST {monitor}/signals/{name} 投递信号。

const terminalStatuses = new Set(["Succeeded", "Failed", "Canceled"]);

/** 返回状态监视器的状态是否为终态。 */
export const isTerminal = operation => terminalStatuses.has(operation?.status);

/** 报告既未完成操作也未提供操作位置的响应。 */
export class OperationError extends Error {
  constructor(response, problem) {
    super(problem?.detail ?? problem?.title ?? `${response.status} ${response.statusText}`);
    this.name = "OperationError";
    this.status = response.status;
    this.problem = problem;
  }
}

export class DurableOperations {
  /**
   * @param {object} [options]
   * @param {Storage} [options.storage] 持久化意图和正在运行的操作；默认为 sessionStorage，
   *   可以在标签页刷新后保留数据。
   * @param {string} [options.prefix] 存储键的前缀。
   * @param {number[]} [options.retryDelays] 请求因网络错误或未携带 Retry-After 的
   *   503 Service Unavailable 而失败时，每次重试前等待的毫秒数。
   * @param {number} [options.wait] 长轮询请求服务器等待的秒数。
   */
  constructor({ storage = sessionStorage, prefix = "durable-operations", retryDelays = [250, 1000, 3000], wait = 30 } = {}) {
    this.storage = storage;
    this.prefix = prefix;
    this.retryDelays = retryDelays;
    this.wait = wait;
  }

  /** 返回意图的持久化键；如果是新意图，则创建一个键。 */
  key(intent) {
    const record = this.#read(`intent:${intent}`) ?? { key: crypto.randomUUID() };
    this.#write(`intent:${intent}`, record);
    return record.key;
  }

  /** 删除意图记录，使其下一次启动时获得一个新键。 */
  forget(intent) {
    this.storage.removeItem(this.#name(`intent:${intent}`));
  }

  /** 返回服务器尚未确认的启动请求：[{ intent, key, request }]。 */
  pending() {
    return this.#entries("intent:")
      .filter(([, record]) => record.request)
      .map(([intent, record]) => ({ intent, ...record }));
  }

  /** 返回辅助工具正在观察的运行中操作：[{ location, intent, data }]。 */
  watching() {
    return Object.entries(this.#read("watching") ?? {}).map(([location, entry]) => ({ location, ...entry }));
  }

  /**
   * 启动某个意图对应的操作，或定位到该意图先前尝试所启动的操作。
   *
   * @param {string} intent 用户意图的名称，例如 `checkout:alice`。
   * @param {(key: string) => { url: string, method?: string, body?: unknown, data?: unknown }} build 根据
   *   意图的键构建请求。辅助工具会持久化请求，因此恢复启动时发送的是同一个请求。
   * @param {object} [options]
   * @param {(attempt: number) => Record<string, string>} [options.headers] 为某次尝试添加请求标头。
   * @param {number} [options.wait] 启动请求要求服务器等待结果的秒数。
   * @returns {Promise<{ key: string, location?: string, operation?: object, result?: unknown, data?: unknown }>}
   *   操作运行中或失败时返回其状态监视器，成功时返回操作结果。
   */
  async start(intent, build, { headers, wait } = {}) {
    const name = `intent:${intent}`;
    const record = this.#read(name) ?? { key: crypto.randomUUID() };
    record.request ??= build(record.key);
    this.#write(name, record);
    return this.#send(intent, record, { headers, wait });
  }

  /**
   * 重新发送服务器尚未确认的启动请求，并携带各自的键，例如启动过程中页面刷新后。
   * 按 pending() 的顺序返回各启动请求的结果。
   */
  async resume() {
    return Promise.all(this.pending().map(({ intent, ...record }) => this.#send(intent, record, {})));
  }

  /**
   * 使用服务器发送事件观察操作，直到其进入终态；事件不可用时使用长轮询。
   * 操作进入终态或服务器不再保留该操作时，删除其观察记录。
   *
   * @param {string} location 操作状态监视器的位置。
   * @param {object} [options]
   * @param {(operation: object) => void} [options.onStatus] 接收每次状态更新。
   * @param {AbortSignal} [options.signal] 停止观察。
   * @returns {Promise<object>} 操作处于终态时的状态监视器。
   * @throws {OperationError} 操作不存在或保留期结束时，状态码为 404。
   */
  async observe(location, { onStatus, signal } = {}) {
    let operation = typeof EventSource === "undefined" ? undefined : await this.#observeEvents(location, onStatus, signal);
    try {
      while (!isTerminal(operation)) {
        signal?.throwIfAborted();
        operation = await this.get(location, { wait: this.wait, signal });
        onStatus?.(operation);
      }
    } catch (error) {
      if (error instanceof OperationError && error.status === 404) this.#unwatch(location);
      throw error;
    }

    this.#unwatch(location);
    return operation;
  }

  /** 读取操作的状态监视器；运行期间执行等待 wait 秒的长轮询。 */
  async get(location, { wait = 0, signal } = {}) {
    const response = await this.#fetch(location, { headers: wait > 0 ? { Prefer: `wait=${wait}` } : {}, signal });
    return this.#readOperation(response);
  }

  /** 携带原因请求取消操作，并返回其状态监视器。 */
  async cancel(location, reason) {
    const response = await this.#fetch(`${location}:cancel`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason }),
    });
    return this.#readOperation(response);
  }

  /**
   * 向操作投递带有 JSON 值的信号。重复投递值相同的信号会成功，因此可以安全重试；
   * 从 key(intent) 派生意图的值，使恢复后的意图发送相同的值。
   */
  async signal(location, name, value) {
    const response = await this.#fetch(`${location}/signals/${encodeURIComponent(name)}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(value),
    });
    if (!response.ok) throw new OperationError(response, await readProblem(response));
  }

  async #send(intent, record, { headers, wait }) {
    const { url, method = "POST", body, data } = record.request;
    const response = await this.#fetch(url, {
      method,
      headers: attempt => ({
        "Idempotency-Key": record.key,
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
        ...(wait > 0 ? { Prefer: `wait=${wait}` } : {}),
        ...headers?.(attempt),
      }),
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    // 服务器已响应，因此持有启动请求的结果，意图的键已完成其用途。
    const location = response.headers.get("Operation-Location") ?? undefined;
    if (response.status === 202 || response.status === 424) {
      const operation = await response.json();
      this.forget(intent);
      if (!isTerminal(operation)) this.#watch(location, { intent, data });
      return { key: record.key, location, operation, data };
    }

    if (response.ok) {
      this.forget(intent);
      const result = response.status === 204 ? undefined : await response.json();
      return { key: record.key, location, result, data };
    }

    // 服务器错误意味着启动尚未得到确认，因此重试时发送同一个键；客户端错误则是最终结果。
    if (response.status < 500) this.forget(intent);
    throw new OperationError(response, await readProblem(response));
  }

  // 观察操作的事件直至进入终态。如果事件流在终态之前关闭（例如服务器不提供事件），
  // 返回最后一次状态或 undefined，以便调用方改用长轮询。
  #observeEvents(location, onStatus, signal) {
    return new Promise(resolve => {
      const events = new EventSource(`${location}/events`);
      let last;
      const finish = () => {
        events.close();
        signal?.removeEventListener("abort", finish);
        resolve(last);
      };
      signal?.addEventListener("abort", finish, { once: true });
      events.addEventListener("status", event => {
        last = JSON.parse(event.data);
        onStatus?.(last);
        if (isTerminal(last)) finish();
      });

      // 浏览器使用 Last-Event-ID 重新连接中断的事件流；浏览器关闭事件流时，事件传输结束。
      events.onerror = () => {
        if (events.readyState === EventSource.CLOSED) finish();
      };
    });
  }

  // 发送请求；发生网络故障或不改变操作状态的 503 Service Unavailable 时，按配置的延迟
  // 或响应的 Retry-After 进行重试。每次尝试的请求头可以不同。
  async #fetch(url, { method = "GET", headers = {}, body, signal } = {}) {
    for (let attempt = 0; ; attempt++) {
      let delay;
      try {
        const response = await fetch(url, { method, headers: typeof headers === "function" ? headers(attempt) : headers, body, signal });
        if (response.status !== 503 || attempt >= this.retryDelays.length) return response;
        const retryAfter = Number(response.headers.get("Retry-After"));
        delay = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : this.retryDelays[attempt];
      } catch (error) {
        if (signal?.aborted || attempt >= this.retryDelays.length) throw error;
        delay = this.retryDelays[attempt];
      }

      await new Promise(resolve => setTimeout(resolve, delay));
    }
  }

  async #readOperation(response) {
    if (!response.ok) throw new OperationError(response, await readProblem(response));
    return response.json();
  }

  #watch(location, entry) {
    if (!location) return;
    const watching = this.#read("watching") ?? {};
    watching[location] = entry;
    this.#write("watching", watching);
  }

  #unwatch(location) {
    const watching = this.#read("watching") ?? {};
    delete watching[location];
    this.#write("watching", watching);
  }

  #entries(kind) {
    const prefix = this.#name(kind);
    const entries = [];
    for (let i = 0; i < this.storage.length; i++) {
      const name = this.storage.key(i);
      if (name?.startsWith(prefix)) entries.push([name.slice(prefix.length), JSON.parse(this.storage.getItem(name))]);
    }

    return entries;
  }

  #name(name) {
    return `${this.prefix}:${name}`;
  }

  #read(name) {
    const value = this.storage.getItem(this.#name(name));
    return value === null ? undefined : JSON.parse(value);
  }

  #write(name, value) {
    this.storage.setItem(this.#name(name), JSON.stringify(value));
  }
}

async function readProblem(response) {
  try {
    return response.headers.get("content-type")?.includes("json") ? await response.json() : { detail: (await response.text()) || undefined };
  } catch {
    return undefined;
  }
}
