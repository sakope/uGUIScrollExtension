using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UGUICustom
{
	[RequireComponent(typeof(ScrollRect))]
	public class ScrollExtension : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IDragHandler
	{
		[SerializeField] private Button     _nextButton; //右送りボタン
		[SerializeField] private Button     _prevButton; //左送りボタン
		[SerializeField] private Transform  _pagenationContainer; //ページネイションコンテナ
		[SerializeField] private GameObject _pagenationIcon;      //ページネイションアイコン
		[SerializeField] private Boolean    _useFlick           = true; //フリック機能を使うか否か
		[SerializeField] private int        _flickDetectionArea = 60; //フリックの際にこの距離だけ指が動いたらフリックと判定する距離
		[SerializeField] private bool       _useAutoSwipe       = false; //自動スワイプするかどうか.
		[SerializeField] private float      _autoSwipeDuration  = 4f; //自動スワイプの間隔.

		public int                TotalPage     { get; private set; }                            // 総ページ数
		public int                LastPageIndex { get { return Mathf.Max(TotalPage - 1, 0); } }  // 最終ページ

		public Action<GameObject, int> onPageMoveStartCallback = delegate {}; // ページの移動開始時び出されるコールバック
		public Action<GameObject, int> onPageMoveEndCallback   = delegate {}; // ページの移動完了時に呼び出されるコールバック


		private ScrollRect       _scrollRect;
		private RectTransform    _scrollRectTransform;
		private List<GameObject> _cachedPagenationIcon = new List<GameObject>();

		private bool          _isInitialized;

		private float         _targetNormalizedPosition;
		private int           _currentPageWhenBeginDrag;
		private List<float>   _positions              = new List<float>();
		private Vector3       _startPosition          = new Vector3();
		private bool          _isFingerReleased       = true;
		private bool          _isScroll               = false;

		private bool          _isFlick                = false;
		private bool          _isDragging             = false;
		private float         _draggingTime           = 0f;
		private float         _autoSwipeTimer         = 0f;

		private float         _SNAP_SPEED             = 12f;    //スナップする際のスピード
		private float         _SNAP_THRESHOLD         = 0.002f; //スナップ吸着しきい値.
		private float         _FLICK_THRESHOLD_TIME   = 0.2f;   //フリックされたと判定する秒数閾値

		public void Initialize()
		{
			_scrollRect          = GetComponent<ScrollRect>();
			_scrollRect.inertia  = false;
			_scrollRectTransform = _scrollRect.content;

			if (_nextButton) _nextButton.onClick.AddListener (() => NextPageByButton());
			if (_prevButton) _prevButton.onClick.AddListener (() => PreviousPageByButton());

			// ページ移動のコールバック設定
			onPageMoveStartCallback += (pageObj, pageIndex) => _ChangeBulletsInfo(pageIndex);
			onPageMoveStartCallback += (pageObj, pageIndex) => _CheckNextPrevButtons(pageIndex);

			InitializePages();
			_isInitialized = true;
		}

		void Update()
		{
			// 初期化が完了するまで待つ
			if (!_isInitialized || TotalPage <= 1)
			{
				return;
			}

			if (_useAutoSwipe)
			{
				_autoSwipeTimer += Time.deltaTime;
			}

			if (_autoSwipeTimer >= _autoSwipeDuration)
			{
				if (!_isScroll && !_isDragging)
				{
					if (CurrentPage() + 1 < TotalPage)
					{
						_GoToPageByIndex(CurrentPage() + 1);
					}
					else
					{
						_GoToPageByIndex(0);
					}
				}

				_autoSwipeTimer = 0f;
			}

			if (_isScroll)
			{
				_scrollRect.horizontalNormalizedPosition = Mathf.Lerp(_scrollRect.horizontalNormalizedPosition, _targetNormalizedPosition, _SNAP_SPEED * Time.deltaTime);

				//目的Positionへ_snapThreshold内の近似値で吸着Fix.
				if (Mathf.Abs(_scrollRect.horizontalNormalizedPosition - _targetNormalizedPosition) < _SNAP_THRESHOLD)
				{
					int currentPage = CurrentPage();
					_scrollRect.horizontalNormalizedPosition = _targetNormalizedPosition;

					onPageMoveEndCallback(_scrollRectTransform.GetChild(currentPage).gameObject, currentPage);
					_autoSwipeTimer = 0f;
					_isScroll = false;
				}
			}

			if (_isDragging)
			{
				_draggingTime += Time.deltaTime;
			}
		}

		/// <summary>
		/// 子要素が増減する場合は再度走らせます.
		/// </summary>
		public void InitializePages()
		{
			_isScroll       = false;
			TotalPage       = _scrollRectTransform.childCount;
			_autoSwipeTimer = 0f;

			_positions.Clear();

			if (TotalPage > 0)
			{
				for (int i = 0; i < TotalPage; ++i)
				{
					_positions.Add((float)i / (float)(TotalPage - 1));
				}
			}

			_scrollRect.horizontalNormalizedPosition = 0;

			_DistributeBulletsInfo();
			_CheckNextPrevButtons(CurrentPage());
		}

		/// <summary>
		/// 指定のページIndexに移動します.第二引数はモーション有無です.
		/// </summary>
		public void GotoPageByIndex(int pageIndex, bool isAnimation = true)
		{
			if (pageIndex >= 0 && pageIndex < TotalPage)
			{
				if (isAnimation)
				{
					_GoToPageByIndex(pageIndex);
				}
				else
				{
					_SkipToPageByIndex(pageIndex);
				}
			}
		}

		/// <summary>
		/// 指定ページへの移動を開始する
		/// </summary>
		private void _GoToPageByIndex(int pageIndex)
		{
			onPageMoveStartCallback(_scrollRectTransform.GetChild(pageIndex).gameObject, pageIndex);
			_isScroll = true;
			_targetNormalizedPosition = _positions[pageIndex];
		}

		/// <summary>
		/// 指定のページIndexへ即座に移動します.
		/// </summary>
		private void _SkipToPageByIndex(int pageIndex)
		{
			GameObject pageObject = _scrollRectTransform.GetChild(pageIndex).gameObject;
			onPageMoveStartCallback(pageObject, pageIndex);
			_scrollRect.horizontalNormalizedPosition = _positions[pageIndex];
			onPageMoveEndCallback(pageObject, pageIndex);
		}

		/// <summary>
		/// 次へボタンを押した時の挙動.
		/// </summary>
		public void NextPageByButton()
		{
			if (CurrentPage() < TotalPage - 1)
			{
				GotoPageByIndex(CurrentPage() + 1);
			}
		}

		/// <summary>
		/// 戻るボタンを押した時の挙動.
		/// </summary>
		public void PreviousPageByButton()
		{
			if (CurrentPage() > 0)
			{
				GotoPageByIndex(CurrentPage() - 1);
			}
		}

		/// <summary>
		/// スワイプ時の次へ挙動.
		/// </summary>
		private void _NextPageBySwipe()
		{
			if (_currentPageWhenBeginDrag < TotalPage - 1)
			{
				GotoPageByIndex(_currentPageWhenBeginDrag + 1);
			}
		}

		/// <summary>
		/// スワイプ時の戻る挙動.
		/// </summary>
		private void _PrevPageBySwipe()
		{
			if (_currentPageWhenBeginDrag > 0)
			{
				GotoPageByIndex(_currentPageWhenBeginDrag - 1);
			}
		}

		/// <summary>
		/// positionsリストから、一番近いpage indexを返す.
		/// </summary>
		private int FindClosestFrom(float start, List<float> positions)
		{
			int pageIndex = 0;
			float distance = Mathf.Infinity;

			int index = 0;

			foreach (float position in _positions)
			{
				if (Mathf.Abs(start - position) < distance)
				{
					distance = Mathf.Abs(start - position);
					pageIndex = index;
				}

				index++;
			}

			return pageIndex;
		}

		/// <summary>
		/// 現在地に最も近い子要素インデックスを返す.
		/// </summary>
		public int CurrentPage()
		{
			//イニシャライズ中のCurrentPage取得に関しては、_scrollRect.NormalizedPositionにバグがある為0を返す.
			//http://forum.unity3d.com/threads/repositioning-scrollrect-through-code-bug-or-wrong-method.270550/
			if (_isInitialized)
			{
				return FindClosestFrom(_scrollRect.horizontalNormalizedPosition, _positions);
			}
			else
			{
				return 0;
			}
		}

		/// <summary>
		/// Toggleコンポーネント群のページネーションを数合わせ生成/破棄.
		/// </summary>
		private void _DistributeBulletsInfo()
		{
			if (!_pagenationContainer)
			{
				return;
			}

			if (TotalPage <= 1)
			{
				foreach (GameObject go in _cachedPagenationIcon)
				{
					go.SetActive(false);
				}

				return;
			}

			if (TotalPage > _cachedPagenationIcon.Count)
			{
				int diffCount = TotalPage - _cachedPagenationIcon.Count;

				for (int i = 0; i < diffCount; i++)
				{
					GameObject obj = GameObject.Instantiate(_pagenationIcon, Vector3.zero, Quaternion.identity) as GameObject;
					obj.transform.SetParent(_pagenationContainer, true);
					obj.transform.localScale = Vector3.one;
					_cachedPagenationIcon.Add(obj);
				}
			}

			var activePagenationIcons = _cachedPagenationIcon.Take(TotalPage);
			var inactivePagenationIcons = _cachedPagenationIcon.Skip(TotalPage);

			foreach (GameObject pagenationIcon in activePagenationIcons)
			{
				pagenationIcon.SetActive(true);
			}

			foreach (GameObject pagenationIcon in inactivePagenationIcons)
			{
				pagenationIcon.SetActive(false);
			}

			_ChangeBulletsInfo(CurrentPage());
		}

		/// <summary>
		/// Toggleコンポーネント群のページネーションを設定.
		/// </summary>
		private void _ChangeBulletsInfo(int currentPage)
		{
			if (_pagenationContainer)
			{
				for (int i = 0; i < _pagenationContainer.childCount; i++)
				{
					_pagenationContainer.GetChild(i).GetComponent<Toggle>().isOn = (currentPage == i) ? true : false;
				}
			}
		}

		/// <summary>
		/// ボタンの必要不必要チェック.
		/// </summary>
		private void _CheckNextPrevButtons(int pageIndex)
		{
			if (!_nextButton || !_prevButton)
			{
				return;
			}

			_nextButton.gameObject.SetActive(pageIndex < TotalPage - 1);
			_prevButton.gameObject.SetActive(pageIndex > 0);
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			_startPosition = _scrollRectTransform.localPosition;
			_draggingTime = 0f;
			_isDragging = true;
			_currentPageWhenBeginDrag = CurrentPage();
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			_isFingerReleased = true;

			if (_scrollRect.horizontal)
			{
				if (_useFlick)
				{
					_isFlick = false;
					_isDragging = false;
					_autoSwipeTimer = 0f;

					if (_draggingTime <= _FLICK_THRESHOLD_TIME)
					{
						if (Math.Abs(_startPosition.x - _scrollRectTransform.localPosition.x) > _flickDetectionArea)
						{
							_isFlick = true;
						}
					}

					if (_isFlick)
					{
						if (_startPosition.x - _scrollRectTransform.localPosition.x > 0)
						{
							_NextPageBySwipe();
						}
						else
						{
							_PrevPageBySwipe();
						}
					}
					else
					{
						_GoToPageByIndex(FindClosestFrom(_scrollRect.horizontalNormalizedPosition, _positions));
					}
				}
				else
				{
					_GoToPageByIndex(FindClosestFrom(_scrollRect.horizontalNormalizedPosition, _positions));
				}
			}
		}

		public void OnDrag(PointerEventData eventData)
		{
			_isScroll = false;

			if (_isFingerReleased)
			{
				OnBeginDrag(eventData);
				_isFingerReleased = false;
			}
		}
	}
}
