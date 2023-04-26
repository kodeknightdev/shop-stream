from collections.abc import Iterator

from sqlalchemy import create_engine
from sqlalchemy.orm import DeclarativeBase, Session, sessionmaker

from .config import get_settings


class Base(DeclarativeBase):
    pass


_engine = None
_session_factory: sessionmaker[Session] | None = None


def init_engine(url: str | None = None, **kwargs):
    global _engine, _session_factory
    _engine = create_engine(url or get_settings().database_url, pool_pre_ping=True, future=True, **kwargs)
    _session_factory = sessionmaker(bind=_engine, expire_on_commit=False)
    return _engine


def get_engine():
    if _engine is None:
        init_engine()
    return _engine


def session_factory() -> sessionmaker[Session]:
    if _session_factory is None:
        init_engine()
    assert _session_factory is not None
    return _session_factory


def get_session() -> Iterator[Session]:
    session = session_factory()()
    try:
        yield session
    finally:
        session.close()
