'use strict';

const cartKey = (userId) => `cart:${userId}`;
const productIndexKey = (productId) => `cart:product:${productId}`;

const round2 = (n) => Math.round(n * 100) / 100;

function summarize(userId, cart) {
  const items = cart?.items || [];
  return {
    userId,
    items,
    itemCount: items.reduce((n, i) => n + i.quantity, 0),
    subtotal: round2(items.reduce((sum, i) => sum + i.unitPrice * i.quantity, 0)),
    hasStalePrices: items.some((i) => i.priceStale),
    updatedAt: cart?.updatedAt || null,
  };
}

// one JSON doc per cart, sliding TTL. cart:product:<id> -> users so price changes
// can find carts without scanning keys
function createCartStore(redis, ttlSeconds) {
  async function load(userId) {
    const raw = await redis.get(cartKey(userId));
    return raw ? JSON.parse(raw) : { items: [], updatedAt: null };
  }

  async function save(userId, cart) {
    cart.updatedAt = new Date().toISOString();
    const pipeline = redis.multi().set(cartKey(userId), JSON.stringify(cart), 'EX', ttlSeconds);
    for (const item of cart.items) {
      pipeline.sadd(productIndexKey(item.productId), userId).expire(productIndexKey(item.productId), ttlSeconds);
    }
    await pipeline.exec();
    return cart;
  }

  return {
    async get(userId) {
      return summarize(userId, await load(userId));
    },

    async addItem(userId, product, quantity, maxQuantity) {
      const cart = await load(userId);
      const existing = cart.items.find((i) => i.productId === product.id);
      if (existing) {
        existing.quantity = Math.min(maxQuantity, existing.quantity + quantity);
        existing.unitPrice = product.price;
        existing.priceStale = false;
        delete existing.latestPrice;
      } else {
        cart.items.push({
          productId: product.id,
          sku: product.sku,
          name: product.name,
          imageUrl: product.imageUrl,
          unitPrice: product.price,
          quantity: Math.min(maxQuantity, quantity),
          priceStale: false,
          addedAt: new Date().toISOString(),
        });
      }
      return summarize(userId, await save(userId, cart));
    },

    async setQuantity(userId, productId, quantity) {
      const cart = await load(userId);
      const item = cart.items.find((i) => i.productId === productId);
      if (!item) return null;
      if (quantity === 0) {
        cart.items = cart.items.filter((i) => i.productId !== productId);
        await redis.srem(productIndexKey(productId), userId);
      } else {
        item.quantity = quantity;
      }
      return summarize(userId, await save(userId, cart));
    },

    async removeItem(userId, productId) {
      return this.setQuantity(userId, productId, 0);
    },

    async clear(userId) {
      const cart = await load(userId);
      const pipeline = redis.multi().del(cartKey(userId));
      for (const item of cart.items) pipeline.srem(productIndexKey(item.productId), userId);
      await pipeline.exec();
    },

    async revalidate(userId, catalog) {
      const cart = await load(userId);
      const changes = [];
      const removed = [];
      for (const item of [...cart.items]) {
        const product = await catalog.getProduct(item.productId);
        if (!product || !product.isActive) {
          removed.push({ productId: item.productId, name: item.name });
          cart.items = cart.items.filter((i) => i.productId !== item.productId);
          continue;
        }
        if (product.price !== item.unitPrice) {
          changes.push({ productId: item.productId, name: item.name, oldPrice: item.unitPrice, newPrice: product.price });
          item.unitPrice = product.price;
        }
        item.priceStale = false;
        delete item.latestPrice;
      }
      await save(userId, cart);
      return { cart: summarize(userId, cart), changes, removed };
    },

    // flag it, don't change it - the customer should see the new price
    async flagPriceChange(productId, newPrice) {
      const userIds = await redis.smembers(productIndexKey(productId));
      let flagged = 0;
      for (const userId of userIds) {
        const cart = await load(userId);
        const item = cart.items.find((i) => i.productId === productId);
        if (!item) {
          await redis.srem(productIndexKey(productId), userId);
          continue;
        }
        if (item.unitPrice !== newPrice) {
          item.priceStale = true;
          item.latestPrice = newPrice;
          await save(userId, cart);
          flagged += 1;
        }
      }
      return flagged;
    },
  };
}

module.exports = { createCartStore, summarize };
